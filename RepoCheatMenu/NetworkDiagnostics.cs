// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace RepoCheatMenu;

/// <summary>
/// Диагностика сетевого состояния: пинг, регион, игроки, реальный трафик Photon.
/// Только чтение статистики клиента - ничего не отправляет и не генерирует трафик.
/// </summary>
internal static class NetworkDiagnostics
{
    private const float UpdateInterval = 1f;

    private static bool _running;
    private static bool _statsEnabledByUs;
    private static float _lastSampleTime;
    private static int _lastIncomingBytes;
    private static int _lastOutgoingBytes;

    public static bool IsConnected { get; private set; }
    public static bool InRoom { get; private set; }
    public static ClientState State { get; private set; } = ClientState.Disconnected;
    public static int Ping { get; private set; }
    public static int PlayersOnline { get; private set; }
    public static string ServerRegion { get; private set; } = "-";
    public static float IncomingTraffic { get; private set; }
    public static float OutgoingTraffic { get; private set; }

    public static void Apply()
    {
        // Check the toggle BEFORE touching PhotonNetwork.NetworkingClient, not after - reading
        // that property at all force-creates a default, unconfigured LoadBalancingClient if the
        // game hasn't set up its own yet (confirmed the hard way: this ticks from Plugin.Awake
        // onward, long before DataDirector configures Photon on a menu/level scene's Start, so
        // it used to fire every single tick from the very first frame regardless of this toggle's
        // value - the old order read the property first and checked the toggle second). Once that
        // premature client exists, the game's real connect sequence finds one "already there" and
        // hangs forever waiting on OnConnectedToMaster - same failure PacketTestHack.Init's own
        // comment documents, just triggered by this file instead since its toggle defaults off
        // but the property read didn't care.
        if (!CheatState.NetworkDiagnostics.Value)
        {
            Stop(null);
            return;
        }

        LoadBalancingPeer peer = PhotonNetwork.NetworkingClient?.LoadBalancingPeer;
        if (peer == null)
        {
            Stop(peer);
            return;
        }

        if (!_running)
        {
            if (!peer.TrafficStatsEnabled)
            {
                peer.TrafficStatsEnabled = true;
                _statsEnabledByUs = true;
            }
            _lastIncomingBytes = TotalBytes(peer.TrafficStatsIncoming);
            _lastOutgoingBytes = TotalBytes(peer.TrafficStatsOutgoing);
            _lastSampleTime = Time.unscaledTime;
            _running = true;
            return;
        }

        float elapsed = Time.unscaledTime - _lastSampleTime;
        if (elapsed < UpdateInterval) return;
        _lastSampleTime = Time.unscaledTime;

        State = PhotonNetwork.NetworkClientState;
        IsConnected = PhotonNetwork.IsConnected;
        InRoom = PhotonNetwork.InRoom;
        Ping = IsConnected ? peer.RoundTripTime : 0;
        ServerRegion = string.IsNullOrEmpty(PhotonNetwork.CloudRegion) ? "-" : PhotonNetwork.CloudRegion;
        PlayersOnline = InRoom && PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.PlayerCount : 0;

        int incoming = TotalBytes(peer.TrafficStatsIncoming);
        int outgoing = TotalBytes(peer.TrafficStatsOutgoing);
        IncomingTraffic = RatePerSecond(incoming - _lastIncomingBytes, elapsed);
        OutgoingTraffic = RatePerSecond(outgoing - _lastOutgoingBytes, elapsed);
        _lastIncomingBytes = incoming;
        _lastOutgoingBytes = outgoing;
    }

    private static void Stop(LoadBalancingPeer peer)
    {
        if (!_running) return;
        _running = false;

        if (_statsEnabledByUs && peer != null)
            peer.TrafficStatsEnabled = false;
        _statsEnabledByUs = false;

        IsConnected = false;
        InRoom = false;
        State = ClientState.Disconnected;
        Ping = 0;
        PlayersOnline = 0;
        ServerRegion = "-";
        IncomingTraffic = 0f;
        OutgoingTraffic = 0f;
    }

    private static int TotalBytes(TrafficStats stats) => stats?.TotalPacketBytes ?? 0;

    private static float RatePerSecond(int deltaBytes, float elapsed)
    {
        if (deltaBytes <= 0 || elapsed <= 0f) return 0f;
        return deltaBytes / elapsed / 1024f;
    }

    public static string GetStatsString()
    {
        if (!CheatState.NetworkDiagnostics.Value)
            return "Диагностика выключена";

        if (!IsConnected)
            return $"Не подключён к серверу (состояние: {State})";

        return $"Регион: {ServerRegion}   Пинг: {Ping} мс   Игроков в комнате: {(InRoom ? PlayersOnline.ToString() : "-")}\n" +
               $"Вход: {IncomingTraffic:F1} КБ/с   Выход: {OutgoingTraffic:F1} КБ/с   Состояние: {State}";
    }
}
