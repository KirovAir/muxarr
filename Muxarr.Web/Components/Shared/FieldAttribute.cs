namespace Muxarr.Web.Components.Shared;

public enum FieldType
{
    Text,
    Url,
    Password
}

[AttributeUsage(AttributeTargets.Property)]
public class FieldAttribute(string label) : Attribute
{
    public string Label { get; } = label;
    public string Placeholder { get; set; } = "";
    public string HelpText { get; set; } = "";
    public FieldType Type { get; set; } = FieldType.Text;
}
