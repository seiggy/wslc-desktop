
namespace WslcDesktop.Containers;

public sealed record ContainerRow(string Name, string Image, string Id, string State, string Command, string Ports, string Labels = "", string Status = "");
