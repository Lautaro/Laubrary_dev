using System;
using System.IO;
using Laubrary.ZTracker.Model;

namespace Laubrary.ZTracker.Editor
{
    /// <summary>Focused regression checks for the recursive drum source catalogue.</summary>
    public static class ZTrackerDrumLibraryCheck
    {
        public static string Execute()
        {
            string root = Path.Combine(Path.GetTempPath(), "ZTrackerDrumLibraryCheck-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Bass Drum", "Deep"));
            Directory.CreateDirectory(Path.Combine(root, "SnareDrum"));
            try
            {
                File.WriteAllBytes(Path.Combine(root, "Bass Drum", "kick.WAV"), Array.Empty<byte>());
                File.WriteAllBytes(Path.Combine(root, "Bass Drum", "Deep", "nested.ogg"), Array.Empty<byte>());
                File.WriteAllBytes(Path.Combine(root, "SnareDrum", "snare.aif"), Array.Empty<byte>());
                File.WriteAllBytes(Path.Combine(root, "SnareDrum", "ignored.txt"), Array.Empty<byte>());

                var all = ZTrackerDrumLibrary.ScanRoot(root);
                Need(all.errors.Count == 0, "Unexpected scan errors: " + all.errors.Count);
                Need(all.files.Count == 3, "Expected all three supported clips, got " + all.files.Count);
                Need(all.files.Exists(f => f.name == "nested" && f.parentFolderTag == "Deep"), "Nested clip did not use its immediate parent folder.");
                Need(DrumTags.Category("Bass Drum") == DrumCategory.Kick, "Space-insensitive folder alias did not classify as Kick.");
                Need(DrumTags.Category("SNARE DRUM") == DrumCategory.Snare, "Case/space-insensitive folder alias did not classify as Snare.");

                var filtered = ZTrackerDrumLibrary.ScanRoot(root, "Bass Drum");
                Need(filtered.files.Count == 1 && filtered.files[0].name == "kick", "Folder search did not match the parent folder.");
                return "PASS recursive supported-file scan, immediate-parent tags, aliases and folder search; 3 clips found.";
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        static void Need(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
