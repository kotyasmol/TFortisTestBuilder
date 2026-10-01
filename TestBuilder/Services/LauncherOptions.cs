using System;
using System.Collections.Generic;

namespace TestBuilder.Services;

/// <summary>RTL launcher contract: session ID, then display name. Neither grants a role.</summary>
public sealed class LauncherOptions
{
    public string SessionId { get; private init; } = string.Empty;
    public string UserName { get; private init; } = string.Empty;
    public string Error { get; private init; } = string.Empty;
    public bool IsValid => Error.Length == 0;

    public static LauncherOptions Parse(string[] args)
    {
        var positional = new List<string>();
        string? session = null, user = null;
        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i].Trim();
            if (argument is "--session" or "--user")
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                    return Invalid("После --session и --user требуется значение.");
                if (argument == "--session") session = args[i].Trim();
                else user = args[i].Trim();
                continue;
            }
            if (argument.StartsWith("--", StringComparison.Ordinal))
                return Invalid("Неизвестный параметр запуска. Используйте --session и --user.");
            positional.Add(argument);
        }
        if (positional.Count > 2 || (positional.Count > 0 && (session != null || user != null)))
            return Invalid("Укажите сессию и имя двумя аргументами либо через --session и --user.");
        session ??= positional.Count > 0 ? positional[0] : string.Empty;
        user ??= positional.Count > 1 ? positional[1] : string.Empty;
        // The legacy protocol embeds the session in a cookie and in a line-based report.
        foreach (var ch in session)
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_' and not '.' and not '{' and not '}')
                return Invalid("Идентификатор сессии содержит недопустимые символы.");
        foreach (var ch in user)
            if (char.IsControl(ch)) return Invalid("Имя пользователя содержит недопустимые символы.");
        return new LauncherOptions { SessionId = session, UserName = user };
    }

    private static LauncherOptions Invalid(string message) => new() { Error = message };
}
