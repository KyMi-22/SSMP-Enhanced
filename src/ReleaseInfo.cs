namespace Silksong.CoopAssist
{
    public static class ReleaseInfo
    {
        // BepInEx reads System.Version. SSMP caps addon versions at 10 characters.
        internal const string PluginVersion = "0.3.0";
        internal const string NetworkVersion = PluginVersion + "-std3";
        public const string Version = PluginVersion + "-standard";
        public const string Revision = "20261115-borrow-fix";
    }
}
