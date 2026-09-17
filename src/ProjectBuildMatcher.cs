using System.IO;

namespace VsixTreeViewer
{
    internal static class ProjectBuildMatcher
    {
        public static bool IsMatch(string trackedProjectPath, string projectUniqueName, string projectFromEvent)
        {
            if (string.IsNullOrWhiteSpace(projectFromEvent))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(projectUniqueName)
                && string.Equals(projectUniqueName, projectFromEvent, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string trackedProject = NormalizePath(trackedProjectPath);
            string eventProject = NormalizePath(projectFromEvent);

            return (!string.IsNullOrEmpty(trackedProject)
                    && !string.IsNullOrEmpty(eventProject)
                    && string.Equals(trackedProject, eventProject, StringComparison.OrdinalIgnoreCase))
                || string.Equals(Path.GetFileName(trackedProjectPath), Path.GetFileName(projectFromEvent), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return null;
            }
        }
    }
}
