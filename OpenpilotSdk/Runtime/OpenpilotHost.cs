using FFMpegCore;

namespace OpenpilotSdk.Runtime
{
    public static class OpenpilotHost
    {
        private static string? _sshKeyOverridePath;
        private static string? _sshKeyPassphraseOverride;

        public static void Configure(string? sshKeyPath = null, string? sshKeyPassphrase = null)
        {
            if (!string.IsNullOrWhiteSpace(sshKeyPath))
            {
                _sshKeyOverridePath = Path.GetFullPath(sshKeyPath);
            }

            _sshKeyPassphraseOverride = NormalizePassphrase(sshKeyPassphrase);

            _ = OpenpilotPaths.ApplicationDataDirectory;
            _ = OpenpilotPaths.TempDirectory;

            ConfigureBundledFfmpeg();
        }

        public static void SetSshKeyPath(string? sshKeyPath)
        {
            _sshKeyOverridePath = string.IsNullOrWhiteSpace(sshKeyPath)
                ? null
                : Path.GetFullPath(sshKeyPath);
        }

        public static void SetSshKeyPassphrase(string? sshKeyPassphrase)
        {
            _sshKeyPassphraseOverride = NormalizePassphrase(sshKeyPassphrase);
        }

        public static string ResolvePrivateSshKeyPath()
        {
            var configuredPath = FirstExistingFile([
                ValidateExplicitPath(_sshKeyOverridePath, "--ssh-key"),
                ValidateExplicitPath(Environment.GetEnvironmentVariable("OPENPILOT_SSH_KEY"), "OPENPILOT_SSH_KEY"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_ed25519"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_rsa"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_ecdsa"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "comma"),
                Path.Combine(OpenpilotPaths.ApplicationDataDirectory, "opensshkey"),
                OpenpilotPaths.GetBundledAssetPath("opensshkey")
            ]);

            if (configuredPath is not null)
            {
                return configuredPath;
            }

            throw new FileNotFoundException(
                "No usable private SSH key was found. Set OPENPILOT_SSH_KEY or pass --ssh-key to point at an existing private key.");
        }

        public static string? ResolvePrivateSshKeyPassphrase()
        {
            return NormalizePassphrase(_sshKeyPassphraseOverride)
                   ?? NormalizePassphrase(Environment.GetEnvironmentVariable("OPENPILOT_SSH_KEY_PASSPHRASE"));
        }

        private static void ConfigureBundledFfmpeg()
        {
            var ffmpegName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
            var ffprobeName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
            var ffmpegPath = OpenpilotPaths.GetBundledAssetPath(ffmpegName);
            var ffprobePath = OpenpilotPaths.GetBundledAssetPath(ffprobeName);

            if (File.Exists(ffmpegPath) && File.Exists(ffprobePath))
            {
                GlobalFFOptions.Configure(options => options.BinaryFolder = OpenpilotPaths.BundledDirectory);
            }
        }

        private static string? ValidateExplicitPath(string? value, string sourceName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var fullPath = Path.GetFullPath(value);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }

            throw new FileNotFoundException($"The SSH key path from {sourceName} does not exist.", fullPath);
        }

        private static string? FirstExistingFile(IEnumerable<string?> candidates)
        {
            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string? NormalizePassphrase(string? value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
