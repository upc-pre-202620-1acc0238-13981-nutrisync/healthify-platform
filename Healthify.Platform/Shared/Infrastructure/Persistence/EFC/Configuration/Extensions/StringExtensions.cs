using Humanizer;

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration.Extensions;

public static class StringExtensions
{
    /// <summary>Inserts an underscore before every uppercase letter: <c>LocalTimestamp</c> to <c>local_timestamp</c>.</summary>
    public static string ToSnakeCase(this string text)
    {
        return new string(Convert(text.GetEnumerator()).ToArray());

        static IEnumerable<char> Convert(CharEnumerator e)
        {
            if (!e.MoveNext()) yield break;

            yield return char.ToLower(e.Current);

            while (e.MoveNext())
                if (char.IsUpper(e.Current))
                {
                    yield return '_';
                    yield return char.ToLower(e.Current);
                }
                else
                {
                    yield return e.Current;
                }
        }
    }

    /// <summary>Pluralises a table name: <c>CareLink</c> to <c>CareLinks</c>.</summary>
    public static string ToPlural(this string text)
    {
        return text.Pluralize(false);
    }
}
