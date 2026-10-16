using System.Reflection;

namespace NS2Pro.App;

internal static class AppIdentity
{
    // Shared with Windows file metadata, shortcuts and installer via Directory.Build.props.
    internal static readonly string DisplayName = typeof(AppIdentity).Assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product;
    internal static string PreviewTitle => DisplayName + " · 引导预览";
}
