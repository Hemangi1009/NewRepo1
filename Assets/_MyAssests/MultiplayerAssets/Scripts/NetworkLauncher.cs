//THIS IS DEFAULT OF MULTIPLAYER SYNC SCENE
//using Fusion;
//using Fusion.Sockets;
//using System;
//using System.Collections.Generic;
//using UnityEngine;

//public class NetworkLauncher : MonoBehaviour, INetworkRunnerCallbacks
//{
//    [SerializeField] private NetworkRunner runnerPrefab;
//    [SerializeField] private NetworkObject avatarPrefab;
//    [SerializeField] private Transform spawnPoint;
//    [SerializeField] private string sessionName = "MyMultiplayerRoom";

//    private NetworkRunner _runner;

//    private async void Start()
//    {
//        Debug.Log($"[NetworkLauncher] Start() called. SessionName='{sessionName}'");

//        _runner = Instantiate(runnerPrefab);
//        _runner.AddCallbacks(this);
//        Debug.Log($"[NetworkLauncher] Runner instantiated: {_runner.name}");

//        var result = await _runner.StartGame(new StartGameArgs
//        {
//            GameMode = GameMode.Shared,
//            SessionName = sessionName,
//            Scene = SceneRef.FromIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex),
//            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
//        });

//        if (result.Ok)
//        {
//            Debug.Log($"[NetworkLauncher] StartGame SUCCESS. Runner.IsRunning={_runner.IsRunning}, LocalPlayer={_runner.LocalPlayer}, SessionName={_runner.SessionInfo.Name}, PlayerCount={_runner.SessionInfo.PlayerCount}");
//        }
//        else
//        {
//            Debug.LogError($"[NetworkLauncher] StartGame FAILED. ShutdownReason={result.ShutdownReason}");
//        }
//    }

//    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
//    {
//        Debug.Log($"[NetworkLauncher] OnPlayerJoined fired. JoinedPlayer={player}, LocalPlayer={runner.LocalPlayer}, IsLocalPlayer={player == runner.LocalPlayer}, TotalPlayers={runner.SessionInfo.PlayerCount}");

//        if (player != runner.LocalPlayer)
//        {
//            Debug.Log($"[NetworkLauncher] Skipping spawn — this is a remote player join event, not us.");
//            return;
//        }

//        Vector3 pos = spawnPoint != null ? spawnPoint.position : Vector3.zero;
//        Debug.Log($"[NetworkLauncher] Spawning avatar for LOCAL player {player} at position {pos}");

//        var avatar = runner.Spawn(avatarPrefab, pos, Quaternion.identity, player);
//        Debug.Log($"[NetworkLauncher] Spawn call complete. AvatarObject={avatar?.name}, NetworkId={avatar?.Id}");
//    }

//    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
//    {
//        Debug.Log($"[NetworkLauncher] OnPlayerLeft fired. Player={player}, RemainingPlayers={runner.SessionInfo.PlayerCount}");
//    }

//    public void OnConnectedToServer(NetworkRunner runner)
//    {
//        Debug.Log($"[NetworkLauncher] OnConnectedToServer. Runner={runner.name}");
//    }

//    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
//    {
//        Debug.LogWarning($"[NetworkLauncher] OnDisconnectedFromServer. Reason={reason}");
//    }

//    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
//    {
//        Debug.LogError($"[NetworkLauncher] OnConnectFailed. Address={remoteAddress}, Reason={reason}");
//    }

//    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
//    {
//        Debug.LogWarning($"[NetworkLauncher] OnShutdown. Reason={shutdownReason}");
//    }

//    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
//    {
//        Debug.Log($"[NetworkLauncher] OnSessionListUpdated. Count={sessionList.Count}");
//        foreach (var s in sessionList)
//        {
//            Debug.Log($"[NetworkLauncher]   Session: Name='{s.Name}', PlayerCount={s.PlayerCount}/{s.MaxPlayers}, IsOpen={s.IsOpen}, IsVisible={s.IsVisible}");
//        }
//    }

//    // --- Required empty callbacks ---
//    public void OnInput(NetworkRunner runner, NetworkInput input) { }
//    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
//    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
//    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
//    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
//    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
//    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
//    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
//    public void OnSceneLoadDone(NetworkRunner runner) { }
//    public void OnSceneLoadStart(NetworkRunner runner) { }
//    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
//    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
//}

using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using UnityEngine;

public class NetworkLauncher : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private NetworkRunner runnerPrefab;
    [SerializeField] private NetworkObject avatarPrefab;

    [Tooltip("Player 1 uses element 0, player 2 uses element 1, and so on.")]
    [SerializeField] private Transform[] spawnPoints;

    [SerializeField] private string sessionName = "MyMultiplayerRoom";

    private NetworkRunner _runner;

    private async void Start()
    {
        Debug.Log($"[NetworkLauncher] Start() called. SessionName='{sessionName}'");

        _runner = Instantiate(runnerPrefab);
        _runner.AddCallbacks(this);
        Debug.Log($"[NetworkLauncher] Runner instantiated: {_runner.name}");

        var result = await _runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = sessionName,
            Scene = SceneRef.FromIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex),
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });

        if (result.Ok)
        {
            Debug.Log($"[NetworkLauncher] StartGame SUCCESS. Runner.IsRunning={_runner.IsRunning}, LocalPlayer={_runner.LocalPlayer}, SessionName={_runner.SessionInfo.Name}, PlayerCount={_runner.SessionInfo.PlayerCount}");
        }
        else
        {
            Debug.LogError($"[NetworkLauncher] StartGame FAILED. ShutdownReason={result.ShutdownReason}");
        }
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[NetworkLauncher] OnPlayerJoined fired. JoinedPlayer={player}, LocalPlayer={runner.LocalPlayer}, IsLocalPlayer={player == runner.LocalPlayer}, TotalPlayers={runner.SessionInfo.PlayerCount}");

        if (player != runner.LocalPlayer)
        {
            Debug.Log("[NetworkLauncher] Skipping spawn - this is a remote player join event, not us.");
            return;
        }

        GetSpawnPose(player, out Vector3 pos, out Quaternion rot);
        Debug.Log($"[NetworkLauncher] Spawning avatar for LOCAL player {player} at position {pos}");

        // Move the local XR rig first so the avatar and the player start at the same place
        if (LocalXROriginProvider.Instance != null)
            LocalXROriginProvider.Instance.TeleportTo(pos, rot);
        else
            Debug.LogWarning("[NetworkLauncher] No LocalXROriginProvider in the scene - XR rig was NOT moved to the spawn point.");

        var avatar = runner.Spawn(avatarPrefab, pos, rot, player);
        Debug.Log($"[NetworkLauncher] Spawn call complete. AvatarObject={avatar?.name}, NetworkId={avatar?.Id}");
    }

    private void GetSpawnPose(PlayerRef player, out Vector3 pos, out Quaternion rot)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            return;
        }

        // In Shared mode PlayerIds go 1, 2, 3... in join order.
        int index = Mathf.Abs(player.PlayerId - 1) % spawnPoints.Length;
        Transform point = spawnPoints[index] != null ? spawnPoints[index] : spawnPoints[0];

        pos = point.position;
        rot = point.rotation;
        Debug.Log($"[NetworkLauncher] PlayerId={player.PlayerId} -> spawn point index {index}");
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[NetworkLauncher] OnPlayerLeft fired. Player={player}, RemainingPlayers={runner.SessionInfo.PlayerCount}");
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log($"[NetworkLauncher] OnConnectedToServer. Runner={runner.name}");
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"[NetworkLauncher] OnDisconnectedFromServer. Reason={reason}");
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError($"[NetworkLauncher] OnConnectFailed. Address={remoteAddress}, Reason={reason}");
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.LogWarning($"[NetworkLauncher] OnShutdown. Reason={shutdownReason}");
    }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        Debug.Log($"[NetworkLauncher] OnSessionListUpdated. Count={sessionList.Count}");
        foreach (var s in sessionList)
        {
            Debug.Log($"[NetworkLauncher]   Session: Name='{s.Name}', PlayerCount={s.PlayerCount}/{s.MaxPlayers}, IsOpen={s.IsOpen}, IsVisible={s.IsVisible}");
        }
    }

    // --- Required empty callbacks ---
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}