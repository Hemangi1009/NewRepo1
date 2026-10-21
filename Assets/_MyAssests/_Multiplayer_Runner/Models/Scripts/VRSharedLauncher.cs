using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using Fusion.Sockets;

[RequireComponent(typeof(NetworkRunner))]
public class VRSharedLauncher : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Room Settings")]
    [SerializeField] private string sessionName = "VR_SharedRoom";
    [SerializeField] private int maxPlayers = 5;

    [Header("Network Avatar")]
    [Tooltip("Drag your NetworkAvatar prefab from the Project window here")]
    [SerializeField] private NetworkPrefabRef avatarPrefab;

    [Header("Spawn Points (optional, in MainMultiplayer scene)")]
    [SerializeField] private Transform[] spawnPoints;

    private static VRSharedLauncher _instance;
    // Public accessor so AvatarLobbyUI can trigger the connect+scene-load from the Start button.
    public static VRSharedLauncher Instance => _instance;

    private NetworkRunner _runner;
    private bool _hasJoined;
    private bool _hasSpawned;
    private string _targetSceneName;

    // Spawn needs BOTH of these. They can arrive in either order (esp. for a late joiner),
    // so we track them separately instead of assuming one callback comes after the other.
    private bool _localPlayerJoined;
    private bool _sceneLoadDone;

    private void Awake()
    {
        // Singleton guard: if a Launcher already exists (e.g. one survived
        // from LobbyScene), destroy this duplicate instead of connecting again.
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning("[VR Launcher] Duplicate Launcher found in this scene � destroying it. Only the original persists.");
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (_instance != this) return; // this was a duplicate, already destroyed in Awake

        _runner = GetComponent<NetworkRunner>();
        _runner.ProvideInput = true;
        _runner.AddCallbacks(this);

        // IMPORTANT: we deliberately do NOT connect to Fusion here.
        // Connecting immediately (while the player is still choosing an avatar
        // in the Lobby) would put them in the same shared room as everyone
        // else right away - and Fusion's networked scene state is shared by
        // the whole room, so the moment ANY player transitions to
        // MainMultiplayer, every other already-connected player gets forced
        // into that scene too, even if they haven't clicked Start yet.
        // Connecting is deferred to JoinAndEnterMultiplayer(), called only
        // once a player actually clicks Start in their own local Lobby UI.
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    /// <summary>
    /// Call this from the Lobby's Start Game button, after avatar selection.
    /// Connects to Fusion and loads straight into the target multiplayer scene
    /// in one step (via StartGameArgs.Scene), so a player only ever joins the
    /// shared room once they're actually ready to enter multiplayer.
    /// </summary>
    public async void JoinAndEnterMultiplayer(string multiplayerSceneName)
    {
        if (_hasJoined)
        {
            Debug.LogWarning("[VR Launcher] Already joined/joining - ignoring duplicate call.");
            return;
        }

        int buildIndex = GetBuildIndexByName(multiplayerSceneName);
        if (buildIndex < 0)
        {
            Debug.LogError($"[VR Launcher] Scene '{multiplayerSceneName}' isn't in Build Settings (File > Build Settings). Add it and try again.");
            return;
        }

        _hasJoined = true;
        _targetSceneName = multiplayerSceneName;

        Debug.Log($"[VR Launcher] Connecting to room '{sessionName}' and loading '{multiplayerSceneName}'...");

        var sceneManager = GetComponent<NetworkSceneManagerDefault>()
            ?? gameObject.AddComponent<NetworkSceneManagerDefault>();

        var sceneInfo = new NetworkSceneInfo();
        sceneInfo.AddSceneRef(SceneRef.FromIndex(buildIndex), LoadSceneMode.Single);

        var result = await _runner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Shared,
            SessionName = sessionName,
            PlayerCount = maxPlayers,
            SceneManager = sceneManager,
            Scene = sceneInfo,
            IsOpen = true,
            IsVisible = true
        });

        if (result.Ok)
        {
            Debug.Log("[VR Launcher] Connected successfully to Shared Room!");
            // Avatar spawn happens in OnSceneLoadDone (below) once Fusion
            // finishes loading the scene, not here.
        }
        else
        {
            Debug.LogError($"[VR Launcher] Failed to join: {result.ShutdownReason}");
            _hasJoined = false; // allow retrying
        }
    }

    private int GetBuildIndexByName(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name == sceneName)
                return i;
        }
        return -1;
    }

    // Safety net: whichever callback lands last, keep retrying until the avatar exists.
    // (OnSceneLoadDone / OnPlayerJoined order differs for a late joiner, and the runner
    // may not report IsRunning yet at the exact moment either callback fires.)
    private void Update()
    {
        if (_instance != this || !_hasJoined || _hasSpawned) return;
        TrySpawnLocalAvatar();
    }

    private void TrySpawnLocalAvatar()
    {
        if (_hasSpawned || _runner == null || !_runner.IsRunning) return;
        if (!_localPlayerJoined || !_sceneLoadDone) return; // wait until we're in the room AND Fusion finished loading the scene

        Vector3 spawnPos;
        Quaternion spawnRot;

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            Transform sp = spawnPoints[_runner.LocalPlayer.PlayerId % spawnPoints.Length];
            spawnPos = sp.position;
            spawnRot = sp.rotation;
        }
        else
        {
            spawnPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            spawnRot = Quaternion.identity;
        }

        _runner.Spawn(avatarPrefab, spawnPos, spawnRot, _runner.LocalPlayer);
        _hasSpawned = true;
        Debug.Log("[VR Launcher] Spawned network avatar!");
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[VR Launcher] Player {player.PlayerId} entered the room.");

        if (player == runner.LocalPlayer)
        {
            _localPlayerJoined = true;
            TrySpawnLocalAvatar();
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[VR Launcher] Player {player.PlayerId} left the room.");
    }

    #region Other Callbacks
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, System.Collections.Generic.List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, System.Collections.Generic.Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, System.ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        // Fusion has now fully finished loading the scene (prefab table ready,
        // scene NetworkObjects attached) - safe to spawn here.
        Debug.Log("[VR Launcher] Fusion finished loading the scene (OnSceneLoadDone).");
        _sceneLoadDone = true;
        TrySpawnLocalAvatar();
    }
    public void OnSceneLoadStart(NetworkRunner runner) { _sceneLoadDone = false; }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    #endregion
}