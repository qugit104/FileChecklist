using System.Windows.Markup;
using FileChecklist.Core;

namespace FileChecklist.Desktop;

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string source) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => Text.T(source);
}
