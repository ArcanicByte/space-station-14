// ReSharper disable CheckNamespace
using Content.Client._Starlight.UserInterface.RichText;
using Content.Client.RichText;

namespace Content.Client.Paper.UI;

public static class PaperFormattableTags
{
    public static readonly Type[] AllowedTags = [..UserFormattableTags.BaseAllowedTags, typeof(LangTagHandler)];
}
