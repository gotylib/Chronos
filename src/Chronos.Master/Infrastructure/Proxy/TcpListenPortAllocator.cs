using System.Net;
using System.Net.Sockets;

namespace Chronos.Master.Infrastructure.Proxy;

internal static class TcpListenPortAllocator
{
    internal static bool IsTcpPortFreeOnHost(int port)
    {
        try
        {
            using var s = new Socket(SocketType.Stream, ProtocolType.Tcp);
            s.Bind(new IPEndPoint(IPAddress.Any, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>Подбирает свободный listen-порт, начиная с <paramref name="preferred"/>.</summary>
    /// <param name="listenMin">Если задан вместе с <paramref name="listenMax"/>, кандидаты только в этом диапазоне (проброс HAProxy в compose); иначе проверка <see cref="IsTcpPortFreeOnHost"/> на процессе Master.</param>
    internal static int? Allocate(int preferred, HashSet<int> reservedByRoutes, int maxScan, int? listenMin, int? listenMax)
    {
        int? loBound = null;
        int? hiBound = null;
        if (listenMin is { } lo
            && listenMax is { } hi
            && lo >= 1
            && hi >= 1
            && lo <= hi
            && hi <= 65535)
        {
            loBound = lo;
            hiBound = hi;
        }

        var useRange = loBound.HasValue;
        var useBindTest = !useRange;

        if (preferred is < 1 or > 65535)
            preferred = 1024;
        if (useRange)
        {
            if (preferred < loBound!.Value)
                preferred = loBound.Value;
            if (preferred > hiBound!.Value)
                preferred = loBound.Value;
        }

        for (var i = 0; i <= maxScan; i++)
        {
            var candidate = preferred + i;
            if (candidate > 65535)
                break;
            if (useRange && (candidate < loBound!.Value || candidate > hiBound!.Value))
                break;
            if (reservedByRoutes.Contains(candidate))
                continue;
            if (useBindTest && !IsTcpPortFreeOnHost(candidate))
                continue;
            return candidate;
        }

        return null;
    }

    internal static bool IsListenInPublishedRange(int port, int? listenMin, int? listenMax) =>
        listenMin is null || listenMax is null || (port >= listenMin && port <= listenMax);
}
