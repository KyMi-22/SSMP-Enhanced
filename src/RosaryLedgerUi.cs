using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Silksong.CoopAssist
{
    public sealed partial class CoopAssistPlugin
    {
        bool ledgerOpen;
        Vector2 ledgerScroll;
        Texture2D ledgerPaper;
        Font ledgerFont;
        GUIStyle ledgerWindow, ledgerText, ledgerTitle, ledgerEdit, ledgerButton;
        bool EconomyOverlayOpen => ledgerOpen || (WalletReady && RosaryConnected && (purchase != null || rosaryAsks.Count != 0));

        bool OpenRosaryLedger()
        {
            if (!WalletReady)
            {
                Say("请在已加载存档、角色存活时打开念珠记账本。");
                return false;
            }
            if (!LoansOpen && !Ledger.Debts.Any())
            {
                Say("房主尚未开启借款，当前存档也没有借款记录。");
                return false;
            }
            optionsOpen = false;
            bindingCapture = null;
            ledgerOpen = true;
            return true;
        }

        void OpenRosaryLedgerFromMenu()
        {
            if (!OpenRosaryLedger() || global::GameManager.instance.GameState != GlobalEnums.GameState.PAUSED) return;
            UIManager.instance.UIClosePauseMenu();
            UIManager.instance.TogglePauseGame();
        }

        void DrawRosaryOptions()
        {
            if (Hosting) loanSetting.Value = GUILayout.Toggle(loanSetting.Value, "房主设置：允许借款购买");
            else if (RosaryConnected) GUILayout.Label(LoansOpen ? "房主已开启借款：可以借，也可以攒钱" : "本房间可攒钱代付");
            if (Down) GUILayout.Label("成茧等待中：房间会自动提亮；按游戏快捷地图键看地图，↑／↓ 切换同场景队友。");
        }

        void DrawRosaryOverlay()
        {
            if (ledgerOpen && Ledger != null)
            {
                InitLedgerUi();
                var width = Mathf.Min(780, Screen.width - 24);
                var height = Mathf.Min(540, Screen.height - 24);
                GUI.Window(20327509, new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height),
                    DrawRosaryLedger, "", ledgerWindow);
                return;
            }
            // The borrow dialogs are centred and drawn themselves; the shop panel must not hide them.
            if (DrawBorrowDialog()) return;
            if (optionsOpen || travelPickerOpen || !WalletReady || !RosaryConnected ||
                (purchase == null && rosaryAsks.Count == 0)) return;
            GUI.Window(20327510, new Rect(Mathf.Max(10, Screen.width - 370), 30, 350,
                Mathf.Min(Screen.height - 40, 230)), DrawRosaryRequests, "队友补差");
        }

        RosaryMessage VisibleRosaryAsk
        {
            get
            {
                foreach (var ask in rosaryAsks.Values) return ask.Message;
                return null;
            }
        }

        int borrowCursor;
        const int BorrowDialogId = 20327511;

        List<SSMP.Api.Client.IClientPlayer> BorrowChoices()
        {
            if (api == null) return new List<SSMP.Api.Client.IClientPlayer>();
            var rows = new List<SSMP.Api.Client.IClientPlayer>();
            foreach (var peer in api.ClientManager.Players)
                if (Checked(peer.Id) && rosaryPeers.ContainsKey(peer.Id)) rows.Add(peer);
            rows.Sort((a, b) => a.Id.CompareTo(b.Id));
            return rows;
        }

        string LenderName(string identity)
        {
            if (api == null) return "队友";
            foreach (var peer in api.ClientManager.Players)
                if (rosaryPeers.TryGetValue(peer.Id, out var id) && id == identity) return peer.Username;
            return "队友";
        }

        // The loan amount is always the shortfall at ask time; the waiting stage shows the stored amount instead.
        int BorrowAmount(Purchase current) => Mathf.Max(0, current.Price - SpendableRosaries);

        void CenterDialog(int id, float height, string title, Action body)
        {
            float width = Mathf.Min(460, Screen.width - 40);
            float h = Mathf.Min(height, Screen.height - 60);
            GUI.ModalWindow(id, new Rect((Screen.width - width) / 2, (Screen.height - h) / 2, width, h), _ => body(), title);
        }

        bool DrawBorrowDialog()
        {
            if (!WalletReady || !RosaryConnected) return false;
            var ask = VisibleRosaryAsk;
            if (ask != null && ask.Mode == RosaryMode.Loan)
            {
                CenterDialog(BorrowDialogId, 190, "队友想借念珠", () =>
                {
                    var peer = api.ClientManager.GetPlayer(ask.Peer);
                    GUILayout.Label((peer?.Username ?? "队友") + " 想借 " + ask.Amount + " 念珠。");
                    if (!string.IsNullOrEmpty(ask.Label)) GUILayout.Label("用途：" + ask.Label);
                    GUILayout.Label("同意后你的念珠会现在扣掉，对方记账还你。");
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("借给他（Enter）")) AnswerRosaryAsk(ask, true);
                    if (GUILayout.Button("拒绝（Esc）")) AnswerRosaryAsk(ask, false);
                    GUILayout.EndHorizontal();
                });
                return true;
            }
            var current = purchase;
            if (current == null || current.Mode != RosaryMode.Loan) return false;
            if (current.Stage == RosaryStage.ConfirmBorrow)
            {
                CenterDialog(BorrowDialogId, 210, "念珠不足", () =>
                {
                    GUILayout.Label("商品：" + current.Name);
                    GUILayout.Label("价格 " + current.Price + " · 现有 " + SpendableRosaries + " · 还差 " + BorrowAmount(current) + " 念珠");
                    GUILayout.Label("要向队友借 " + BorrowAmount(current) + " 念珠吗？");
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("借款（Enter）")) ConfirmBorrow();
                    if (GUILayout.Button("返回商店（Esc）")) DeclineBorrow();
                    GUILayout.EndHorizontal();
                });
                return true;
            }
            if (current.Stage == RosaryStage.ChoosePeer)
            {
                var peers = BorrowChoices();
                if (borrowCursor >= peers.Count) borrowCursor = Mathf.Max(0, peers.Count - 1);
                CenterDialog(BorrowDialogId, Mathf.Min(420, 170 + Mathf.Max(1, peers.Count) * 34), "选择借钱的队友", () =>
                {
                    GUILayout.Label("商品：" + current.Name);
                    GUILayout.Label("价格 " + current.Price + " · 现有 " + SpendableRosaries + " · 借款 " + BorrowAmount(current) + " 念珠");
                    if (peers.Count == 0) GUILayout.Label("没有可以借款的队友。");
                    for (int i = 0; i < peers.Count; i++)
                    {
                        var peer = peers[i];
                        var row = (i == borrowCursor ? "▶ " : "    ") + peer.Username + (peer.IsInLocalScene ? "（同图）" : "（异图）");
                        if (GUILayout.Button(row, GUILayout.Height(30))) { borrowCursor = i; AskRosaries(peer, RosaryMode.Loan); }
                    }
                    GUILayout.Label("↑／↓ 选择　Enter 确认　Esc 返回商店");
                    if (GUILayout.Button("返回商店（Esc）")) DeclineBorrow();
                });
                return true;
            }
            CenterDialog(BorrowDialogId, 170, "等待队友同意", () =>
            {
                GUILayout.Label("已向 " + LenderName(current.Peer) + " 借 " + current.Amount + " 念珠，等待同意…");
                GUILayout.Label("对方同意后会自动买下这件商品并扣款。");
                if (GUILayout.Button("取消借款")) CancelRosaryPurchase();
            });
            return true;
        }

        // Arrow keys choose the lender and Enter confirms, as the game's own lists do.
        void TickRosaryDialog()
        {
            if (!WalletReady || !RosaryConnected) return;
            var ask = VisibleRosaryAsk;
            if (ask != null && ask.Mode == RosaryMode.Loan)
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) AnswerRosaryAsk(ask, true);
                else if (Input.GetKeyDown(KeyCode.Escape)) AnswerRosaryAsk(ask, false);
                return;
            }
            var current = purchase;
            if (current == null || current.Mode != RosaryMode.Loan) return;
            if (current.Stage == RosaryStage.ConfirmBorrow)
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) ConfirmBorrow();
                else if (Input.GetKeyDown(KeyCode.Escape)) DeclineBorrow();
                return;
            }
            if (current.Stage != RosaryStage.ChoosePeer) return;
            var peers = BorrowChoices();
            if (Input.GetKeyDown(KeyCode.Escape)) { DeclineBorrow(); return; }
            if (peers.Count == 0) return;
            if (borrowCursor >= peers.Count) borrowCursor = peers.Count - 1;
            if (Input.GetKeyDown(KeyCode.DownArrow)) borrowCursor = (borrowCursor + 1) % peers.Count;
            else if (Input.GetKeyDown(KeyCode.UpArrow)) borrowCursor = (borrowCursor - 1 + peers.Count) % peers.Count;
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                AskRosaries(peers[borrowCursor], RosaryMode.Loan);
        }

        void DrawRosaryRequests(int id)
        {
            var ask = VisibleRosaryAsk;
            if (ask != null)
            {
                var peer = api.ClientManager.GetPlayer(ask.Peer);
                var loan = ask.Mode == RosaryMode.Loan;
                GUILayout.Label((peer?.Username ?? "队友") + (loan ? " 想借 " : "请求攒钱补差 ") + ask.Amount + " 念珠。");
                GUILayout.Label(ask.Label);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(loan ? "借给他" : "同意付款")) AnswerRosaryAsk(ask, true);
                if (GUILayout.Button("拒绝")) AnswerRosaryAsk(ask, false);
                GUILayout.EndHorizontal();
                return;
            }
            var current = purchase;
            if (current == null || current.Mode == RosaryMode.Loan) return;
            GUILayout.Label(current.Name + " · 还差 " + Mathf.Max(0, current.Price - rosaryPlayer.geo) + " 念珠");
            if (current.Grant != null)
            {
                if (current.Mode == RosaryMode.Bench)
                {
                    if (GUILayout.Button("确认解锁长椅") && !RosaryPurchaseHooks.ConfirmBench(current.BenchBox))
                    {
                        CancelRosaryPurchase();
                        SayChat("长椅付款窗口已变化，代付将退回。");
                    }
                }
                else GUILayout.Label("队友已同意。回到商店，重新确认这件商品。");
            }
            else if (current.Request != null) GUILayout.Label("等待队友同意…");
            else foreach (var peer in api.ClientManager.Players)
            {
                if (!IsRosaryPeer(peer)) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label(peer.Username);
                if (GUILayout.Button("攒钱")) AskRosaries(peer, current.Mode);
                if (current.Mode != RosaryMode.Bench && LoansOpen && GUILayout.Button("借款")) AskRosaries(peer, RosaryMode.Loan);
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("取消")) CancelRosaryPurchase();
        }

        void DrawRosaryLedger(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("念珠记账本", ledgerTitle);
            if (GUILayout.Button("合上", ledgerButton, GUILayout.Width(64))) ledgerOpen = false;
            GUILayout.EndHorizontal();
            GUILayout.Label("逾期时间 8 分 48 秒  ·  逾期加收 3 念珠", ledgerText);
            GUILayout.BeginHorizontal();
            GUILayout.Label("位置", ledgerText, GUILayout.Width(140));
            GUILayout.Label("队友", ledgerText, GUILayout.Width(108));
            GUILayout.Label("欠款", ledgerText, GUILayout.Width(62));
            GUILayout.Label("借款", ledgerText, GUILayout.Width(62));
            GUILayout.Label("还款", ledgerText);
            GUILayout.EndHorizontal();
            ledgerScroll = GUILayout.BeginScrollView(ledgerScroll);
            foreach (var row in Ledger.Debts.Where(row => !row.Hidden)
                .OrderByDescending(row => row.Due).ThenByDescending(row => row.Id))
            {
                GUILayout.BeginHorizontal();
                var position = GUILayout.TextField(row.Place ?? "", 128, ledgerEdit, GUILayout.Width(140));
                if (position != row.Place && CanWriteWallet) { row.Place = position; rosaryDirty = true; }
                GUILayout.Label(row.Name, ledgerText, GUILayout.Width(108));
                GUILayout.Label(row.Outgoing ? "—" : row.Balance.ToString(), ledgerText, GUILayout.Width(62));
                GUILayout.Label(row.Outgoing ? row.Amount.ToString() : "—", ledgerText, GUILayout.Width(62));
                if (row.Balance > 0)
                {
                    if (row.Outgoing) GUILayout.Label("待收 " + row.Balance + " · 已收 " + row.Repaid, ledgerText);
                    else if (GUILayout.Button("还款 · 已还 " + row.Repaid, ledgerButton))
                    {
                        if (!CanWriteWallet) SayChat("存档正在保存或角色不可操作，请稍后再还款。");
                        else RepayRosaries(row);
                    }
                }
                else
                {
                    GUILayout.Label(row.Refunded ? "已撤销" : "已结清", ledgerText);
                    if (GUILayout.Button("删除", ledgerButton, GUILayout.Width(58)) && CanWriteWallet)
                    { row.Hidden = true; rosaryDirty = true; }
                }
                GUILayout.EndHorizontal();
                var line = GUILayoutUtility.GetLastRect();
                if (Event.current.type == EventType.Repaint && row.Balance == 0)
                {
                    var previous = GUI.color;
                    GUI.color = new Color(.34f, .17f, .08f, .7f);
                    GUI.DrawTexture(new Rect(line.x, line.center.y, Mathf.Min(line.width, 435), 1), Texture2D.whiteTexture);
                    GUI.color = previous;
                }
            }
            // Loans the lender already paid for but nobody confirmed: visible, and withdrawable.
            foreach (var pending in Ledger.Transfers.Values.Where(row =>
                row.Outgoing && row.State == RosaryTransferState.Offered).OrderBy(row => row.Id))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(pending.Name, ledgerText, GUILayout.Width(108));
                GUILayout.Label("待对方确认 · " + pending.Amount + " 念珠", ledgerText);
                if (GUILayout.Button("撤回并退回", ledgerButton)) RefundOfferedLoan(pending, "借款已撤回，念珠已退回。");
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        void InitLedgerUi()
        {
            if (ledgerPaper != null) return;
            var path = Path.Combine(Path.GetDirectoryName(typeof(CoopAssistPlugin).Assembly.Location), "Fonts", "AaXuanYuanTi-2.ttf");
            if (File.Exists(path)) ledgerFont = new Font(path);
            ledgerPaper = CreateLedgerPaper();
            var ink = new Color(.25f, .105f, .045f);
            ledgerWindow = new GUIStyle(GUI.skin.window) {
                padding = new RectOffset(33, 33, 30, 34), border = new RectOffset(27, 27, 27, 27)
            };
            ledgerWindow.normal.background = ledgerWindow.onNormal.background = ledgerPaper;
            ledgerWindow.hover.background = ledgerWindow.onHover.background = ledgerPaper;
            ledgerWindow.active.background = ledgerWindow.onActive.background = ledgerPaper;
            ledgerText = new GUIStyle(GUI.skin.label) { font = ledgerFont ?? uiFont, fontSize = 16 };
            ledgerText.normal.textColor = ink;
            ledgerTitle = new GUIStyle(ledgerText) { fontSize = 32 };
            ledgerEdit = new GUIStyle(ledgerText) { clipping = TextClipping.Clip };
            ledgerButton = new GUIStyle(GUI.skin.button) { font = ledgerText.font, fontSize = 15 };
        }

        static Texture2D CreateLedgerPaper()
        {
            const int width = 96, height = 96;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            var grain = new System.Random(301);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var curl = Mathf.Sin(x * .14f) * 1.8f + Mathf.Sin(x * .36f) * .8f;
                var vertical = Mathf.Min(y - 3 - curl, height - 4 - y + curl);
                var horizontal = Mathf.Min(x - 2, width - 3 - x) + Mathf.Sin(y * .2f);
                var edge = Mathf.Min(vertical, horizontal);
                var fold = .06f * Mathf.Exp(-Mathf.Pow((vertical - 8f) / 3.2f, 2));
                var shade = .24f * Mathf.Exp(-Mathf.Max(0, edge) / 4) - fold + (float)grain.NextDouble() * .06f;
                pixels[y * width + x] = new Color(.87f - shade, .68f - shade, .45f - shade, Mathf.Clamp01(edge));
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        void DestroyRosaryUi()
        {
            if (ledgerPaper != null) Destroy(ledgerPaper);
            if (ledgerFont != null) Destroy(ledgerFont);
        }
    }
}
