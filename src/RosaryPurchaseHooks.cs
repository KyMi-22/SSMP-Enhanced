using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using SSMP.Api.Client;
using UnityEngine;

namespace Silksong.CoopAssist
{
    internal static class RosaryPurchaseHooks
    {
        internal const string PatchId = "silksong.coopassist.rosarypurchase";
        static readonly MethodInfo RequiredItems = AccessTools.Method(typeof(ShopItemStats), "HasRequiredItems");
        static readonly MethodInfo SelectedSimpleItem = AccessTools.Method(typeof(SimpleShopMenu), "GetCurrentSelectedItem");
        static readonly MethodInfo SelectYes = AccessTools.Method(typeof(YesNoBox), "SelectYes");
        static readonly FieldInfo CurrencyQueue = AccessTools.Field(typeof(CurrencyManager), "currencyQueue");
        static readonly ConditionalWeakTable<object, RosaryTransfer> Contributions = new ConditionalWeakTable<object, RosaryTransfer>();
        static readonly ConditionalWeakTable<Fsm, BenchConfirmation> BenchConfirmations = new ConditionalWeakTable<Fsm, BenchConfirmation>();
        static readonly Type ScenePath = typeof(ClientAddon).Assembly.GetType("SSMP.Game.Client.ScenePath", true);
        static readonly MethodInfo GetScenePath = AccessTools.Method(ScenePath, "Get", new[] { typeof(Transform) });
        static CoopAssistPlugin Plugin => CoopAssistPlugin.Instance;

        sealed class BenchConfirmation
        {
            internal Fsm Bench;
            internal object Token;
            internal int Price;
        }

        internal static void Install()
        {
            var hooks = new Harmony(PatchId);
            var native = typeof(ClientAddon).Assembly.GetType("SSMP.Game.Client.Save.CoopSave", true);
            var dialogue = AccessTools.Inner(typeof(DialogueYesNoBox), "<>c__DisplayClass33_0");
            Patch(typeof(ShopItemStats), "BuyFail", postfix: nameof(OfferShopItem));
            Patch(typeof(ShopItemStats), "CanBuy", postfix: nameof(CanBuyShopItem));
            Patch(typeof(ShopItem), "SetPurchased", prefix: nameof(PayForShopItem), finalizer: nameof(AfterShopPurchase));
            hooks.Patch(AccessTools.Method(typeof(ShopItem), "SetPurchased"),
                transpiler: new HarmonyMethod(typeof(RosaryPurchaseHooks), nameof(RouteGeoPayment)));
            Patch(typeof(SimpleShopMenu), "OnSubmitPressed", postfix: nameof(OfferSimpleItem));
            hooks.Patch(AccessTools.Method(typeof(SimpleShopMenu), "OnSubmitPressed"),
                transpiler: new HarmonyMethod(typeof(RosaryPurchaseHooks), nameof(AllowFundedSimpleItem)));
            Patch(typeof(SimpleShopMenu), "ConfirmYes", prefix: nameof(PayForSimpleItem));
            hooks.Patch(AccessTools.Method(typeof(SimpleShopMenu), "ConfirmYes"),
                transpiler: new HarmonyMethod(typeof(RosaryPurchaseHooks), nameof(RouteCurrencyPayment)),
                finalizer: new HarmonyMethod(typeof(RosaryPurchaseHooks), nameof(ClearPaymentScope)));
            Patch(typeof(YesNoBox), "OnAppeared", postfix: nameof(OfferBench));
            Patch(typeof(DialogueYesNoBox), "get_InactiveYesText", postfix: nameof(CanPayBench));
            Patch(dialogue, "<Open>b__1", prefix: nameof(ConfirmFundedBench));
            hooks.Patch(AccessTools.Method(typeof(TakeCurrency), "OnEnter"),
                prefix: new HarmonyMethod(typeof(RosaryPurchaseHooks), nameof(PayForBench)),
                transpiler: new HarmonyMethod(typeof(RosaryPurchaseHooks), nameof(RouteCurrencyPayment)),
                finalizer: new HarmonyMethod(typeof(RosaryPurchaseHooks), nameof(ClearPaymentScope)));
            Patch(native, "OnInteractionSwitchState", prefix: nameof(ValidateShopConfirmation));
            Patch(native, "AfterSharedPurchase", postfix: nameof(BindShopPayment));
            Patch(native, "Refund", prefix: nameof(BeforeRefund), finalizer: nameof(AfterRefund));
            Patch(typeof(PlayerData), "AddGeo", postfix: nameof(AfterGain));
            hooks.Patch(AccessTools.Method(typeof(global::GameManager), "SaveGame",
                new[] { typeof(int), typeof(Action<bool>), typeof(bool), typeof(AutoSaveName) }),
                prefix: new HarmonyMethod(typeof(RosaryPurchaseHooks), nameof(BeforeNativeSave)));

            void Patch(Type type, string method, string prefix = null, string postfix = null, string finalizer = null)
            {
                HarmonyMethod Hook(string name) => name == null ? null :
                    new HarmonyMethod(typeof(RosaryPurchaseHooks), name) { priority = Priority.Last };
                hooks.Patch(AccessTools.Method(type, method), prefix: Hook(prefix), postfix: Hook(postfix), finalizer: Hook(finalizer));
            }
        }

        static bool Money(ShopItem item) => item != null && !item.IsPurchased && item.CurrencyType == CurrencyType.Money;
        static bool HasItems(ShopItemStats stats) => (bool)RequiredItems.Invoke(stats, null);
        internal static int SpendableRosaries(PlayerData player)
        {
            if (player == null) return 0;
            var slot = MoneyQueueSlot();
            int queued = slot != null ? (int)Value(slot, "amount") : 0;
            return (int)Math.Max(0, (long)player.geo + Math.Min(0, queued));
        }

        static object MoneyQueueSlot() =>
            CurrencyQueue.GetValue(null) is System.Collections.IList queue && queue.Count > 0 ? queue[0] : null;

        static void OfferShopItem(ShopItemStats __instance)
        {
            var item = __instance.Item;
            if (Money(item) && HasItems(__instance)) Plugin?.OfferRosaryPurchase(item, item.Cost, item.DisplayName, false);
        }

        static void CanBuyShopItem(ShopItemStats __instance, ref bool __result)
        {
            var item = __instance.Item;
            if (!__result && Money(item) && Plugin?.HasRosaryGrant(item, item.Cost) == true && HasItems(__instance))
                __result = true;
        }

        static void ValidateShopConfirmation(Fsm __1, ref FsmState __2)
        {
            if (__2?.Name != "Deduct Geo and set PD" || __1.Name != "Confirm Control") return;
            var itemObject = __1.Variables.FindFsmGameObject("Item GameObject")?.Value;
            if (itemObject == null || !itemObject.TryGetComponent<ShopItemStats>(out var stats)) return;
            var item = stats.Item;
            if (item != null && item.IsPurchased && Plugin?.WasResumedPurchase(item) == true)
            {
                if (__1.GetState("Cancel") is FsmState resumed) __2 = resumed;
                return;
            }
            if (Plugin?.MapSharingActive != true) return;
            if (!Money(item) || SpendableRosaries(PlayerData.instance) >= item.Cost ||
                Plugin.HasRosaryGrant(item, item.Cost)) return;
            if (__1.GetState("Cancel") is FsmState cancel) __2 = cancel;
        }

        // Prefix/transpiler store the payment target here; the paired finalizer reads and clears it on the same thread.
        [ThreadStatic] static RosaryTransfer shopContribution;
        [ThreadStatic] static object paymentTarget;
        [ThreadStatic] static Fsm paymentBench;
        [ThreadStatic] static (ShopItem Item, RosaryTransfer Row) pendingShopPayment;

        static void PayForShopItem(ShopItem __instance, bool __runOriginal)
        {
            if (__runOriginal && Money(__instance) && Plugin?.HasRosaryGrant(__instance, __instance.Cost) == true)
                paymentTarget = __instance;
        }

        static Exception AfterShopPurchase(ShopItem __instance, Exception __exception)
        {
            if (shopContribution != null) pendingShopPayment = (__instance, shopContribution);
            shopContribution = null;
            return ClearPaymentScope(__exception);
        }

        static ISimpleShopItem SimpleItem(SimpleShopMenu menu) =>
            SelectedSimpleItem.Invoke(menu, null) as ISimpleShopItem;

        static void OfferSimpleItem(SimpleShopMenu __instance)
        {
            if (!__instance.isActiveAndEnabled || Value(__instance, "state")?.ToString() != "ItemList") return;
            var item = SimpleItem(__instance);
            if (item != null && !item.DelayPurchase())
                Plugin?.OfferRosaryPurchase(item, item.GetCost(), item.GetDisplayName(), false, null, __instance);
        }

        // Resume the purchase the borrow flow interrupted, now that the borrowed rosaries are in the wallet.
        internal static bool ResumePurchase(object target, object menu, int price)
        {
            try
            {
                // The shop stays open under the borrow dialog, so buying is one call once the money is here.
                if (menu is SimpleShopMenu simple && simple != null && simple.isActiveAndEnabled)
                {
                    simple.ConfirmYes();
                    return true;
                }
                // The second parameter is a sub-item index, not the cost; -1 means "no sub item".
                if (target is ShopItem item && item != null && !item.IsPurchased &&
                    item.CurrencyType == CurrencyType.Money)
                {
                    item.SetPurchased(null, -1);
                    CoopAssistPlugin.Instance?.MarkResumedPurchase(item);
                    return true;
                }
            }
            catch (Exception error)
            {
                CoopAssistPlugin.Instance?.LogWarning("[Rosaries] Could not finish the purchase: " + error.Message);
            }
            return false;
        }

        // Back to the vendor's item list when the borrow flow is dropped.
        internal static void CloseNativeConfirm(object menu)
        {
            try
            {
                if (menu is SimpleShopMenu simple && simple != null && simple.isActiveAndEnabled &&
                    Value(simple, "state")?.ToString() == "Confirm")
                    AccessTools.Method(typeof(SimpleShopMenu), "ConfirmNo", Type.EmptyTypes).Invoke(simple, null);
            }
            catch (Exception) { }
        }

        static IEnumerable<CodeInstruction> AllowFundedSimpleItem(IEnumerable<CodeInstruction> code)
        {
            var instructions = code.ToList();
            var geo = AccessTools.Field(typeof(PlayerData), nameof(PlayerData.geo));
            var index = instructions.FindIndex(instruction => instruction.LoadsField(geo));
            if (index < 0) throw new MissingFieldException("SimpleShopMenu balance check");
            var balance = instructions[index];
            balance.opcode = OpCodes.Ldloc_0;
            balance.operand = null;
            instructions.Insert(index + 1, new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(RosaryPurchaseHooks), nameof(SimpleBalance))));
            return instructions;
        }

        static int SimpleBalance(PlayerData player, ISimpleShopItem item)
        {
            if (item != null && !item.DelayPurchase() && Plugin?.HasRosaryGrant(item, item.GetCost()) == true)
                return item.GetCost();
            return player.geo;
        }

        static bool PayForSimpleItem(SimpleShopMenu __instance)
        {
            var item = SimpleItem(__instance);
            if (item == null || PlayerData.instance.geo >= item.GetCost()) return true;
            if (Plugin?.HasRosaryGrant(item, item.GetCost()) == true) { paymentTarget = item; return true; }
            if (Plugin?.OfferRosaryPurchase(item, item.GetCost(), item.GetDisplayName(), false, null, __instance) == true)
                return false;
            AccessTools.Method(typeof(SimpleShopMenu), "ConfirmNo", Type.EmptyTypes).Invoke(__instance, null);
            return false;
        }

        internal static bool ConfirmBench(object target)
        {
            if (!(target is DialogueYesNoBox box) || !(BenchContext(box, out _, out var price) is object token) ||
                Value(token, "items") != null || Value(token, "willGetItem") != null ||
                Plugin?.HasRosaryGrant(token, price) != true) return false;
            SelectYes.Invoke(box, null);
            return true;
        }

        static object Value(object instance, string name) => ReflectionCache.Get(instance, name);

        static object BenchContext(DialogueYesNoBox box, out Fsm fsm, out int price)
        {
            price = 0;
            fsm = null;
            if (!(Value(box, "currentYes") is Action wrapped) || !(Value(wrapped.Target, "yes") is Action yes) ||
                !(yes.Target is FsmStateAction action)) return null;
            var candidate = action.Fsm;
            if (FindBench(candidate) == null ||
                !(Value(wrapped.Target, "currencyType") is CurrencyType currency) || currency != CurrencyType.Money ||
                !(Value(wrapped.Target, "currencyAmount") is int amount) || amount <= 0) return null;
            fsm = candidate;
            price = amount;
            return wrapped.Target;
        }

        static Fsm FindBench(Fsm prompt)
        {
            if (prompt?.GameObject == null) return null;
            for (var owner = prompt.GameObject.transform; owner != null; owner = owner.parent)
            {
                if (!owner.name.Contains("Toll Bench")) continue;
                foreach (var component in owner.GetComponents<PlayMakerFSM>())
                {
                    var bench = component.Fsm;
                    if (bench.Name != "Behaviour (special)" || bench.ActiveStateName != "Confirm" ||
                        bench.GetState("Flip to Bench") == null) continue;
                    foreach (var action in bench.ActiveState.Actions)
                        if (action is RunFSM && ReferenceEquals(Value(action, "runFsm"), prompt)) return bench;
                }
            }
            return null;
        }

        static void OfferBench(YesNoBox __instance)
        {
            if (__instance is DialogueYesNoBox box && BenchContext(box, out _, out var price) is object token &&
                Value(token, "items") == null && Value(token, "willGetItem") == null)
                Plugin?.OfferRosaryPurchase(token, price, "解锁付费长椅", true, box);
        }

        static void CanPayBench(DialogueYesNoBox __instance, ref string __result)
        {
            if (!string.IsNullOrEmpty(__result) && BenchContext(__instance, out _, out var price) is object token &&
                Value(token, "items") == null && Value(token, "willGetItem") == null &&
                Plugin?.HasRosaryGrant(token, price) == true) __result = "";
        }

        static bool ConfirmFundedBench(object __instance)
        {
            if (Plugin == null || !(Value(__instance, "yes") is Action yes) ||
                !(yes.Target is FsmStateAction action) || !(Value(__instance, "currencyAmount") is int cost) ||
                !Plugin.IsFundedBench(__instance)) return true;
            if (SpendableRosaries(PlayerData.instance) >= cost)
            {
                Plugin.CancelUnusedBenchContribution();
                return true;
            }
            var bench = FindBench(action.Fsm);
            if (bench != null && Plugin.HasRosaryGrant(__instance, cost))
            {
                BenchConfirmations.Remove(action.Fsm);
                BenchConfirmations.Add(action.Fsm, new BenchConfirmation { Bench = bench, Token = __instance, Price = cost });
                Plugin.ExtendBenchConfirmation();
                return true;
            }
            (Value(__instance, "no") as Action)?.Invoke();
            return false;
        }

        static bool PayForBench(TakeCurrency __instance)
        {
            var bench = FindBench(__instance.Fsm);
            if (!BenchConfirmations.TryGetValue(__instance.Fsm, out var confirmation))
            {
                if (bench == null) return true;
                if (__instance.CurrencyType.Value is CurrencyType type && type == CurrencyType.Money &&
                    Plugin?.MapSharingActive == true && SpendableRosaries(PlayerData.instance) < __instance.Amount.Value)
                {
                    __instance.Fsm.SetState("Cancel");
                    return false;
                }
                paymentBench = bench;
                return true;
            }
            BenchConfirmations.Remove(__instance.Fsm);
            if (Plugin != null && confirmation.Bench == FindBench(__instance.Fsm) &&
                __instance.CurrencyType.Value is CurrencyType currency && currency == CurrencyType.Money &&
                __instance.Amount.Value == confirmation.Price)
            {
                if (Plugin.HasRosaryGrant(confirmation.Token, confirmation.Price))
                {
                    paymentTarget = confirmation.Token;
                    paymentBench = confirmation.Bench;
                    return true;
                }
                if (PlayerData.instance.geo >= confirmation.Price) return true;
            }
            __instance.Fsm.SetState("Cancel");
            return false;
        }

        static IEnumerable<CodeInstruction> RouteGeoPayment(IEnumerable<CodeInstruction> code) =>
            ReplaceCharge(code, nameof(CurrencyManager.TakeGeo), nameof(ChargeGeo));

        static IEnumerable<CodeInstruction> RouteCurrencyPayment(IEnumerable<CodeInstruction> code) =>
            ReplaceCharge(code, nameof(CurrencyManager.TakeCurrency), nameof(ChargeCurrency));

        static IEnumerable<CodeInstruction> ReplaceCharge(IEnumerable<CodeInstruction> code, string original, string replacement)
        {
            var instructions = code.ToList();
            var method = AccessTools.Method(typeof(CurrencyManager), original);
            var target = instructions.Single(instruction => instruction.Calls(method));
            target.operand = AccessTools.Method(typeof(RosaryPurchaseHooks), replacement);
            return instructions;
        }

        static void ChargeGeo(int price) => ChargeCurrency(price, CurrencyType.Money, true);

        static void ChargeCurrency(int price, CurrencyType type, bool show)
        {
            var target = paymentTarget;
            var bench = paymentBench;
            ClearPaymentScope(null);
            if (type != CurrencyType.Money || Plugin == null || !(MoneyQueueSlot() is object slot))
            {
                CurrencyManager.TakeCurrency(price, type, show);
                return;
            }
            if (target == null)
            {
                if (bench != null && SpendableRosaries(PlayerData.instance) >= price && RecordBench(bench, price, null))
                {
                    ChargeImmediately(price, type, show, slot, price);
                    return;
                }
                CurrencyManager.TakeCurrency(price, type, show);
                return;
            }
            var row = Plugin.UseRosaryGrant(target, price);
            if (row == null)
            {
                CurrencyManager.TakeCurrency(price, type, show);
                return;
            }
            // Let SSMP record the original price for its existing duplicate-payment refund.
            // Then undo the queued debit and settle only the buyer's share immediately.
            ChargeImmediately(price, type, show, slot, price - row.Amount);
            if (target is ShopItem) shopContribution = row;
            if (bench != null) RecordBench(bench, price - row.Amount, row.Id);
        }

        static void ChargeImmediately(int price, CurrencyType type, bool show, object slot, int personal)
        {
            var queuedBefore = (int)Value(slot, "amount");
            CurrencyManager.TakeCurrency(price, type, show);
            ReflectionCache.Set(slot, "amount", queuedBefore);
            Plugin.ChargePersonalRosaries(personal);
        }

        static Exception ClearPaymentScope(Exception __exception)
        {
            paymentTarget = null;
            paymentBench = null;
            return __exception;
        }

        static bool RecordBench(Fsm bench, int personal, string contribution)
        {
            if (bench.GameObject == null || GetScenePath == null) return false;
            var path = (string)GetScenePath.Invoke(null, new object[] { bench.GameObject.transform });
            var unique = bench.GameObject.scene.name + "/" + path;
            using var sha = SHA256.Create();
            var key = new Guid(sha.ComputeHash(Encoding.UTF8.GetBytes(unique)).Take(16).ToArray()).ToString("N");
            return Plugin.RecordTollPayment(key, personal, contribution);
        }

        static void BindShopPayment(object __instance, object __0)
        {
            // Cleared on every call: SSMP also reports ordinary purchases here with a null record.
            var pending = pendingShopPayment;
            pendingShopPayment = default;
            if (__0 == null || !(Value(__0, "Item") is ShopItem item) ||
                !(Value(__0, "Flag") is string flag)) return;
            var row = shopContribution ?? (pending.Item == item ? pending.Row : null);
            if (row != null && Value(__instance, "_localPurchases") is System.Collections.IDictionary purchases &&
                purchases[flag] is object interaction)
            {
                Contributions.Remove(interaction);
                Contributions.Add(interaction, row);
            }
        }

        sealed class RefundState
        {
            internal RosaryTransfer Row;
            internal List<(CurrencyType, int)> Money;
            internal int Index, Original;
        }

        static void BeforeRefund(object __0, out RefundState __state)
        {
            __state = null;
            if (!Contributions.TryGetValue(__0, out var row) || Value(__0, "Refunded") is true ||
                !(Value(__0, "Currency") is List<(CurrencyType, int)> money)) return;
            var index = money.FindIndex(item => item.Item1 == CurrencyType.Money && item.Item2 >= row.Amount);
            if (index < 0) return;
            __state = new RefundState { Row = row, Money = money, Index = index, Original = money[index].Item2 };
            if (row.Mode != RosaryMode.Loan)
                money[index] = (CurrencyType.Money, __state.Original - row.Amount);
        }

        static Exception AfterRefund(object __0, RefundState __state, Exception __exception)
        {
            if (__state == null) return __exception;
            __state.Money[__state.Index] = (CurrencyType.Money, __state.Original);
            if (Value(__0, "Refunded") is true) Plugin?.UndoRosaryShare(__state.Row);
            return __exception;
        }

        static void AfterGain(PlayerData __instance, int __0) => Plugin?.CollectAfterRosaryGain(__instance, __0);
        static void BeforeNativeSave() => Plugin?.FlushRosaryLedgerForNativeSave();

        internal static void ClearPayments()
        {
            pendingShopPayment = default;
        }
    }
}
