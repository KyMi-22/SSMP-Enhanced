using System.IO;
using System.Linq;
namespace Silksong.CoopAssist
{
    public static class SkinSelection
    {
        // No active marker means vanilla. Exactly one pack is selected, following Customizer's first-pack rule.
        public static string[] ActiveDirectories(string root) => Directory.Exists(root)
            ? Directory.GetDirectories(root).Where(dir=>File.Exists(Path.Combine(dir,"active.txt"))).Take(1).ToArray()
            : new string[0];
    }
}
