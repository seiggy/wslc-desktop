namespace WslcDesktop.Images;

public readonly record struct PullProgress(string Status, string Id, long CurrentBytes, long TotalBytes);
