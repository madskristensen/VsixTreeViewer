using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VsixTreeViewer
{
    internal sealed class VsixOutputLocatorOptions
    {
        public string ProjectDirectory { get; init; }
        public string OutputPath { get; init; }
        public string TargetFramework { get; init; }
        public string TargetVsixContainer { get; init; }
        public string TargetVsixContainerName { get; init; }
        public IReadOnlyCollection<string> PreferredFileNames { get; init; } = Array.Empty<string>();
    }

    internal static class VsixOutputLocator
    {
        public static string GetOutputDirectory(VsixOutputLocatorOptions options)
        {
            if (options == null
                || string.IsNullOrWhiteSpace(options.ProjectDirectory)
                || string.IsNullOrWhiteSpace(options.OutputPath))
            {
                return null;
            }

            return Path.GetFullPath(Path.Combine(options.ProjectDirectory, options.OutputPath));
        }

        public static string FindVsix(VsixOutputLocatorOptions options)
        {
            if (options == null)
            {
                return null;
            }

            string targetContainer = ResolveTargetContainer(options);
            if (!string.IsNullOrWhiteSpace(targetContainer) && File.Exists(targetContainer))
            {
                return targetContainer;
            }

            string outputDirectory = GetOutputDirectory(options);
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
            {
                return null;
            }

            var candidateDirectories = new List<string> { outputDirectory };
            if (!string.IsNullOrWhiteSpace(options.TargetFramework))
            {
                candidateDirectories.Add(Path.Combine(outputDirectory, options.TargetFramework));
            }

            string[] candidates = candidateDirectories
                .Where(Directory.Exists)
                .SelectMany(path => Directory.GetFiles(path, "*.vsix", SearchOption.TopDirectoryOnly))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (candidates.Length == 0)
            {
                candidates = Directory.GetFiles(outputDirectory, "*.vsix", SearchOption.AllDirectories);
            }

            var preferredNames = new HashSet<string>(
                options.PreferredFileNames ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            return candidates
                .OrderByDescending(path => preferredNames.Contains(Path.GetFileName(path)))
                .ThenByDescending(File.GetLastWriteTimeUtc)
                .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static string ResolveTargetContainer(VsixOutputLocatorOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.TargetVsixContainer))
            {
                return Path.IsPathRooted(options.TargetVsixContainer)
                    ? Path.GetFullPath(options.TargetVsixContainer)
                    : Path.GetFullPath(Path.Combine(options.ProjectDirectory, options.TargetVsixContainer));
            }

            string outputDirectory = GetOutputDirectory(options);
            return string.IsNullOrWhiteSpace(outputDirectory) || string.IsNullOrWhiteSpace(options.TargetVsixContainerName)
                ? null
                : Path.GetFullPath(Path.Combine(outputDirectory, options.TargetVsixContainerName));
        }
    }
}
