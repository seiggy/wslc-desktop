using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace WslcDesktop.Settings;

public sealed record SettingDef(string Key, string Section, string Label, string Help, string[]? Choices = null);
