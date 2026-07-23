using System.Net;
using System.Net.Http;
using System;

namespace Alife.Function.QChat;

public static class QChatRuntimeHealthReasonClassifier
{
    public static string ForOneBotConnectionFailure() => "OneBotUnavailable";

    public static string ForModelFailure(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
                return "ModelAuthRejected";
        }

        return "HealthProbeFailed";
    }
}
