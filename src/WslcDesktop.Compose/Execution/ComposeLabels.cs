namespace WslcDesktop.Compose.Execution;

public static class ComposeLabels
{
    public const string Project = "com.wslc.compose.project";

    public const string Service = "com.wslc.compose.service";

    /// <summary>Extracts the compose project and service from the comma-joined label string that `wslc list` prints.</summary>
    public static bool TryParse(string? labels, out string project, out string service)
    {
        project = service = string.Empty;
        if (string.IsNullOrEmpty(labels))
        {
            return false;
        }

        foreach (var part in labels.Split(','))
        {
            var eq = part.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }

            var key = part[..eq].Trim();
            if (key == Project)
            {
                project = part[(eq + 1)..].Trim();
            }
            else if (key == Service)
            {
                service = part[(eq + 1)..].Trim();
            }
        }

        return project.Length > 0;
    }
}
