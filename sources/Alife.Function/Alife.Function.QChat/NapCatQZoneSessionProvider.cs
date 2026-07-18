using System;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public sealed class NapCatQZoneSessionProvider(IOneBotActionInvoker invoker) : IQZoneSessionProvider
{
    public async Task<QZoneSession> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NapCatQZoneCookieResponse? response = await invoker.CallActionAsync<NapCatQZoneCookieResponse>(
            "get_cookies", new { domain = "qzone.qq.com" });
        string cookies = response?.Cookies?.Trim() ?? "";
        string bkn = response?.Bkn?.Trim() ?? "";
        if (cookies.Length == 0)
            throw new QZoneSessionUnavailableException("qzone_cookie_unavailable");
        if (bkn.Length == 0)
            throw new QZoneSessionUnavailableException("qzone_bkn_unavailable");

        return new QZoneSession(ParseAccountId(cookies), cookies, bkn);
    }

    static long ParseAccountId(string cookies)
    {
        long? accountId = ParseCookieAccountId(cookies, "p_uin")
                          ?? ParseCookieAccountId(cookies, "uin");
        return accountId
               ?? throw new QZoneSessionUnavailableException("qzone_account_unavailable");
    }

    static long? ParseCookieAccountId(string cookies, string name)
    {
        foreach (string segment in cookies.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0
                || string.Equals(segment[..separatorIndex], name, StringComparison.Ordinal) == false)
                continue;

            string value = segment[(separatorIndex + 1)..];
            if (value.Length > 1
                && value[0] == 'o'
                && long.TryParse(value.AsSpan(1), out long accountId)
                && accountId > 0)
                return accountId;
        }

        return null;
    }
}
