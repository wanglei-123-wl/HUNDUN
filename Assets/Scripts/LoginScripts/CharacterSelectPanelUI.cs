using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.UI;

public class CharacterSelectPanelUI : MonoBehaviour
{
    private const string SelectedCharacterIdKey = "selected_character_id";
    private const string SelectedCharacterDbIdKey = "selected_character_db_id";
    private const string SelectedCharacterNameKey = "selected_character_name";
    private const string SelectedCharacterTemplateKey = "selected_character_template";

    [Header("Character List UI")]
    [SerializeField] private GameObject characterPanel;
    [SerializeField] private CharacterRoleSwitcher roleSwitcher;
    [SerializeField] private Button openCreateCharacterButton;
    [SerializeField] private GameObject createCharacterPanel;
    [SerializeField] private CreateCharacterPanelUI createCharacterPanelUI;
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button deleteCharacterButton;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private TMP_Text nicknameText;

    [Header("Character Sprites")]
    [SerializeField] private List<CharacterSpriteMapping> characterSprites = new List<CharacterSpriteMapping>();
    [SerializeField] private Sprite unknownCharacterSprite;
    [SerializeField] private int maxCharacterCount = 3;

    private const string ListCharactersPath = "/api/characters/list";
    private const string CreateCharacterPath = "/api/character/create";

    [Header("HTTP Character API")]
    [SerializeField] private AuthApiClient authApiClient;
    [SerializeField] private string selectCharacterPath = "/api/character/select";
    [SerializeField] private string deleteCharacterPath = "/api/character/delete";

    [Header("Scene")]
    [SerializeField] private string successSceneName = RemoteGameContent.WorldSceneName;
    [SerializeField] private float gateEnterWorldTimeout = 10f;

    [Header("Loading View")]
    [SerializeField] private WorldEntryLoadingView loadingView;

    private readonly List<CharacterData> characters = new List<CharacterData>();
    private bool listRequestInFlight;
    private bool selectRequestInFlight;
    private bool createRequestInFlight;
    private bool deleteRequestInFlight;
    private bool isLoadingScene;
    private bool createPanelEventSubscribed;
    private GateEntryCoordinator gateEntry;

    public int CharacterCount => characters.Count;
    public CharacterData CurrentCharacter
    {
        get
        {
            int index = roleSwitcher == null ? -1 : roleSwitcher.CurrentIndex;
            return index >= 0 && index < characters.Count ? characters[index] : null;
        }
    }

    private void Awake()
    {
        ResolveReferences();
        EnsureSpriteMappings();
        EnsureCreatePanelController();
        ClearCharacterListView();

        if (createCharacterPanel != null)
        {
            createCharacterPanel.SetActive(false);
        }

        RefreshButtonState();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureCreatePanelController();

        if (roleSwitcher != null)
        {
            roleSwitcher.OnIndexChanged += HandleRoleIndexChanged;
        }

        if (openCreateCharacterButton != null)
        {
            openCreateCharacterButton.onClick.AddListener(OpenCreateCharacterPanel);
        }

        if (startGameButton != null)
        {
            startGameButton.onClick.AddListener(StartGame);
        }

        if (createCharacterPanelUI != null)
        {
            SubscribeCreatePanel();
        }
    }

    private void OnDisable()
    {
        if (gateEntry != null && !gateEntry.IsCompleted)
        {
            GateEntryHandoff.Clear();
            NetMessageDispatcher.Instance?.ClearDeferredWorldMessages();
            NetworkManager.Instance?.Disconnect();
            AuthSession.Instance?.ClearSession();
        }
        gateEntry?.Dispose();
        gateEntry = null;

        if (roleSwitcher != null)
        {
            roleSwitcher.OnIndexChanged -= HandleRoleIndexChanged;
        }

        if (openCreateCharacterButton != null)
        {
            openCreateCharacterButton.onClick.RemoveListener(OpenCreateCharacterPanel);
        }

        if (startGameButton != null)
        {
            startGameButton.onClick.RemoveListener(StartGame);
        }

        if (createCharacterPanelUI != null)
        {
            UnsubscribeCreatePanel();
        }
    }

    public void InitializeAfterLogin()
    {
        ResolveReferences();
        EnsureSpriteMappings();
        EnsureCreatePanelController();
        ClearCharacterListView();

        if (characterPanel != null)
        {
            characterPanel.SetActive(true);
        }

        if (createCharacterPanel != null)
        {
            createCharacterPanel.SetActive(false);
        }

        RefreshCharacterList(string.Empty, showLoadingView: true);
    }

    internal void ShowEntryRecovery(string message, long retryCharacterDbId)
    {
        if (loadingView == null)
        {
            ReturnToLoginView(message, clearSession: true);
            return;
        }

        loadingView.ShowFailure(message, () => RetryGameSession(retryCharacterDbId),
            () => LogoutSelectedGameSession(message), "返回登录");
    }

    public void RefreshCharacterList()
    {
        RefreshCharacterList("");
    }

    public void RefreshCharacterList(string preferredCharacterId)
    {
        RefreshCharacterList(preferredCharacterId, showLoadingView: false);
    }

    private void RefreshCharacterList(string preferredCharacterId, bool showLoadingView)
    {
        if (listRequestInFlight || selectRequestInFlight || createRequestInFlight || deleteRequestInFlight || isLoadingScene)
        {
            return;
        }

        if (!TryGetSession(out AuthSession session))
        {
            ClearCharacterListView();
            if (showLoadingView)
                ReturnToLoginView("登录状态已失效，请重新登录。");
            else
                ShowMessage("登录状态已失效，请重新登录。");
            return;
        }

        listRequestInFlight = true;
        ClearCharacterListView();
        ShowMessage("Loading character list...");

        ResolveReferences();
        if (authApiClient == null)
        {
            listRequestInFlight = false;
            ClearCharacterListView();
            ShowCharacterListFailure("角色列表服务不可用，请重试。",
                preferredCharacterId, showLoadingView);
            RefreshButtonState();
            return;
        }

        CharacterListRequest request = new CharacterListRequest
        {
            channel = "Character",
            userId = session.UserId,
            username = session.Username
        };

        if (showLoadingView && loadingView != null)
            loadingView.Show("正在读取角色列表");

        authApiClient.SendAuthorizedRequest(ListCharactersPath, request, result =>
        {
            if (this == null)
                return;

            if (result == null || !result.success)
            {
                listRequestInFlight = false;
                ClearCharacterListView();
                ShowCharacterListFailure(string.IsNullOrWhiteSpace(result?.message)
                    ? "读取角色列表失败，请重试。" : result.message,
                    preferredCharacterId, showLoadingView);
                return;
            }

            HandleCharacterListHttpResult(result, preferredCharacterId);
            if (showLoadingView && loadingView != null)
                loadingView.Hide();
        });
    }

    private void ShowCharacterListFailure(string message, string preferredCharacterId,
        bool showLoadingView)
    {
        ShowMessage(message);
        if (showLoadingView && loadingView != null)
        {
            loadingView.ShowFailure(message,
                () => RefreshCharacterList(preferredCharacterId, showLoadingView: true),
                () => ReturnToLoginView(message, clearSession: true), "返回登录");
        }
    }

    private void RetryGameSession(long characterDbId)
    {
        AuthSession auth = AuthSession.Instance;
        string characterId = PlayerPrefs.GetString(SelectedCharacterIdKey, "");
        if (auth == null || !auth.HasValidGameSession
            || !long.TryParse(PlayerPrefs.GetString(SelectedCharacterDbIdKey, ""), out long selectedDbId)
            || selectedDbId <= 0 || selectedDbId != characterDbId || string.IsNullOrWhiteSpace(characterId))
        {
            ReturnToLoginView("原角色的游戏会话已失效，请重新登录。", clearSession: true);
            return;
        }

        loadingView.Show("正在重连游戏服务器");
        StartCoroutine(EnterWorldBeforeLoading(new CharacterData
        {
            characterId = characterId,
            characterDbId = characterDbId
        }));
    }

    private void LogoutSelectedGameSession(string message)
    {
        AuthSession auth = AuthSession.Instance;
        if (auth == null)
        {
            ReturnToLoginView(message, clearSession: true);
            return;
        }
        if (auth.IsLoggingOut) return;
        isLoadingScene = true;
        RefreshButtonState();
        auth.LogoutGameSession(() =>
        {
            if (this == null) return;
            isLoadingScene = false;
            RefreshButtonState();
            ReturnToLoginView(message);
        });
    }

    private void ReturnToLoginView(string message, bool clearSession = false)
    {
        if (clearSession) AuthSession.Instance?.ClearSession();
        if (loadingView != null) loadingView.Hide();
        LoginUITMP login = GetComponentInParent<LoginUITMP>();
        if (login != null)
            login.ReturnToLoginView(message);
        else
            ShowMessage(message);
    }

    public void OpenCreateCharacterPanel()
    {
        if (listRequestInFlight || selectRequestInFlight || createRequestInFlight || deleteRequestInFlight || isLoadingScene)
        {
            return;
        }

        if (characters.Count >= maxCharacterCount)
        {
            ShowMessage("Character limit reached.");
            return;
        }

        EnsureSpriteMappings();
        EnsureCreatePanelController();
        SubscribeCreatePanel();

        if (createCharacterPanelUI != null)
        {
            createCharacterPanelUI.Show(characterSprites);
        }
        else if (createCharacterPanel != null)
        {
            createCharacterPanel.SetActive(true);
        }
    }

    public void StartGame()
    {
        if (listRequestInFlight || selectRequestInFlight || createRequestInFlight || deleteRequestInFlight || isLoadingScene)
        {
            return;
        }

        CharacterData character = CurrentCharacter;
        if (character == null || character.characterDbId <= 0 || string.IsNullOrWhiteSpace(character.characterId))
        {
            ShowMessage("No character is available.");
            return;
        }

        if (!TryGetSession(out AuthSession session))
        {
            ReturnToLoginView("登录状态已失效，请重新登录。");
            return;
        }

        SaveSelectedCharacter(character);

        selectRequestInFlight = true;
        RefreshButtonState();
        ShowMessage("Selecting character...");

        ResolveReferences();
        if (authApiClient == null)
        {
            selectRequestInFlight = false;
            ShowSelectFailure("角色确认服务不可用，请重试。");
            RefreshButtonState();
            return;
        }

        CharacterSelectRequest request = new CharacterSelectRequest
        {
            channel = "Character",
            characterDbId = character.characterDbId,
            characterId = character.characterId,
            userId = session.UserId,
            username = session.Username
        };

        Debug.Log("[Auth] Select loginToken=" + AuthSession.MaskToken(session.LoginToken));
        if (loadingView != null)
            loadingView.Show("正在确认角色");
        authApiClient.SendAuthorizedRequest(selectCharacterPath, request, result => HandleSelectCharacterHttpResult(result, character));
    }

    public void DeleteCurrentCharacter()
    {
        DeleteCharacter(CurrentCharacter, result =>
        {
            string message = result == null
                ? "Delete character failed."
                : result.message;
            ShowMessage(string.IsNullOrWhiteSpace(message) ? "Delete character failed." : message);
        });
    }

    public void DeleteCharacter(CharacterData character, Action<AuthResult> onComplete)
    {
        if (listRequestInFlight || selectRequestInFlight || createRequestInFlight || deleteRequestInFlight || isLoadingScene)
        {
            onComplete?.Invoke(AuthResult.Fail("Character request is busy."));
            return;
        }

        if (character == null || string.IsNullOrWhiteSpace(character.characterId))
        {
            onComplete?.Invoke(AuthResult.Fail("No character is available."));
            return;
        }

        if (!TryGetSession(out AuthSession session))
        {
            onComplete?.Invoke(AuthResult.Fail("Login session is missing."));
            return;
        }

        deleteRequestInFlight = true;
        RefreshButtonState();
        ShowMessage("Deleting character...");

        ResolveReferences();
        if (authApiClient == null)
        {
            deleteRequestInFlight = false;
            RefreshButtonState();
            onComplete?.Invoke(AuthResult.Fail("Auth service is missing."));
            return;
        }

        CharacterDeleteRequest request = new CharacterDeleteRequest
        {
            channel = "Character",
            characterDbId = character.characterDbId,
            characterId = character.characterId,
            userId = session.UserId,
            confirm = true
        };

        authApiClient.SendAuthorizedRequest(
            deleteCharacterPath,
            request,
            result => HandleDeleteCharacterHttpResult(result, onComplete));
    }

    public void CreateCharacterFromPanel(CharacterSpriteMapping mapping, string nickname)
    {
        CreateCharacter(mapping, nickname);
    }

    private void CreateCharacter(CharacterSpriteMapping mapping, string nickname)
    {
        Debug.Log("Create character panel request received.");

        if (mapping == null || string.IsNullOrWhiteSpace(mapping.characterId))
        {
            if (createCharacterPanelUI != null)
            {
                createCharacterPanelUI.ShowMessage("Please select a character.");
            }

            return;
        }

        nickname = nickname == null ? "" : nickname.Trim();
        if (string.IsNullOrWhiteSpace(nickname))
        {
            if (createCharacterPanelUI != null)
            {
                createCharacterPanelUI.SetSubmitting(false);
                createCharacterPanelUI.ShowMessage("Please enter a character nickname.");
            }

            return;
        }

        if (listRequestInFlight || selectRequestInFlight || createRequestInFlight || deleteRequestInFlight || isLoadingScene)
        {
            if (createCharacterPanelUI != null)
            {
                createCharacterPanelUI.SetSubmitting(false);
            }

            return;
        }

        if (characters.Count >= maxCharacterCount)
        {
            if (createCharacterPanelUI != null)
            {
                createCharacterPanelUI.SetSubmitting(false);
                createCharacterPanelUI.ShowMessage("Character limit reached.");
            }

            return;
        }

        if (!TryGetSession(out AuthSession session))
        {
            if (createCharacterPanelUI != null)
            {
                createCharacterPanelUI.SetSubmitting(false);
                createCharacterPanelUI.ShowMessage("Login session is missing.");
            }

            return;
        }

        createRequestInFlight = true;
        RefreshButtonState();

        ResolveReferences();
        if (authApiClient == null)
        {
            createRequestInFlight = false;
            if (createCharacterPanelUI != null)
            {
                createCharacterPanelUI.SetSubmitting(false);
                createCharacterPanelUI.ShowMessage("Auth service is missing.");
            }

            RefreshButtonState();
            return;
        }

        CharacterCreateRequest request = new CharacterCreateRequest
        {
            channel = "Character",
            characterId = mapping.characterId,
            roleId = mapping.characterId,
            userId = session.UserId,
            nickname = nickname
        };

        Debug.Log("Create character HTTP request: userId=" + session.UserId + ", characterId=" + mapping.characterId + ", nickname=" + nickname);

        authApiClient.SendAuthorizedRequest(CreateCharacterPath, request, result => HandleCreateCharacterHttpResult(result, mapping));
    }

    private void HandleCreateCharacterHttpResult(AuthResult result, CharacterSpriteMapping fallbackMapping)
    {
        if (result == null || !result.success)
        {
            createRequestInFlight = false;
            if (createCharacterPanelUI != null)
            {
                createCharacterPanelUI.SetSubmitting(false);
                createCharacterPanelUI.ShowMessage(result == null ? "Bad create character response." : result.message);
            }

            RefreshButtonState();
            return;
        }

        string createdCharacterId = "";
        if (result.character != null && !string.IsNullOrWhiteSpace(result.character.characterId))
        {
            createdCharacterId = result.character.characterId;
        }
        else if (!string.IsNullOrWhiteSpace(result.characterId))
        {
            createdCharacterId = result.characterId;
        }
        else if (fallbackMapping != null)
        {
            createdCharacterId = fallbackMapping.characterId;
        }

        createRequestInFlight = false;

        if (!string.IsNullOrWhiteSpace(createdCharacterId))
        {
            CharacterData createdCharacter = result.character ?? new CharacterData();
            if (createdCharacter.characterDbId <= 0)
            {
                createdCharacter.characterDbId = result.characterDbId;
            }

            if (string.IsNullOrWhiteSpace(createdCharacter.characterId))
            {
                createdCharacter.characterId = createdCharacterId;
            }

            if (string.IsNullOrWhiteSpace(createdCharacter.characterName))
            {
                createdCharacter.characterName = createdCharacterId;
            }

            if (string.IsNullOrWhiteSpace(createdCharacter.templateId) && fallbackMapping != null)
            {
                createdCharacter.templateId = fallbackMapping.characterId;
            }

            SaveSelectedCharacter(createdCharacter);
        }

        if (createCharacterPanelUI != null)
        {
            createCharacterPanelUI.Hide();
        }

        ShowMessage(string.IsNullOrWhiteSpace(result.message) ? "Character created." : result.message);
        RefreshCharacterList(createdCharacterId);
    }

    private void HandleDeleteCharacterHttpResult(AuthResult result, Action<AuthResult> onComplete)
    {
        deleteRequestInFlight = false;

        if (result == null || !result.success)
        {
            AuthResult failedResult = result ?? AuthResult.Fail("Bad delete character response.");
            ShowMessage(failedResult.message);
            RefreshButtonState();
            onComplete?.Invoke(failedResult);
            return;
        }

        ClearSavedSelectedCharacter();
        ShowMessage(string.IsNullOrWhiteSpace(result.message) ? "Character deleted." : result.message);
        RefreshCharacterList();
        onComplete?.Invoke(result);
    }

    private void HandleCharacterListHttpResult(AuthResult result, string preferredCharacterId)
    {
        listRequestInFlight = false;

        characters.Clear();
        List<Sprite> displaySprites = new List<Sprite>();
        CharacterData[] serverCharacters = result.characters ?? Array.Empty<CharacterData>();

        for (int index = 0; index < serverCharacters.Length && characters.Count < maxCharacterCount; index++)
        {
            CharacterData character = serverCharacters[index];
            if (character == null || character.characterDbId <= 0 || string.IsNullOrWhiteSpace(character.characterId))
            {
                continue;
            }

            characters.Add(character);
            displaySprites.Add(FindCharacterSprite(character));
        }

        int initialIndex = FindCharacterIndex(preferredCharacterId);
        if (initialIndex < 0)
        {
            initialIndex = 0;
        }

        if (characters.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(preferredCharacterId))
            {
                ClearSavedSelectedCharacter();
            }

            ClearCharacterListView();
            ShowMessage("当前账号没有角色，请创建角色。");
        }
        else
        {
            roleSwitcher?.SetSprites(displaySprites, initialIndex);

            if (!string.IsNullOrWhiteSpace(preferredCharacterId) && initialIndex >= 0 && initialIndex < characters.Count)
            {
                SaveSelectedCharacter(characters[initialIndex]);
            }

            ShowCurrentCharacterMessage();
        }

        RefreshButtonState();
    }

    private void HandleSelectCharacterHttpResult(AuthResult result, CharacterData fallbackCharacter)
    {
        if (this == null)
            return;

        if (result == null || !result.success)
        {
            selectRequestInFlight = false;
            ShowSelectFailure(string.IsNullOrWhiteSpace(result?.message)
                ? "确认角色失败，请重试。" : result.message);
            RefreshButtonState();
            return;
        }

        AuthSession session = AuthSession.Instance;
        if (session == null || !session.SaveGameSession(result.gameSessionToken, result.expiresAt))
        {
            selectRequestInFlight = false;
            RefreshButtonState();
            ReturnToLoginView("游戏会话已失效，请重新登录。");
            return;
        }

        CharacterData selectedCharacter = GetValidSelectedCharacter(result, fallbackCharacter);
        if (!SaveSelectedCharacter(selectedCharacter))
        {
            session.ClearSession();
            selectRequestInFlight = false;
            RefreshButtonState();
            ReturnToLoginView("角色资料不完整，请重新登录。");
            return;
        }

        ShowMessage(string.IsNullOrWhiteSpace(result.message) ? "Character selected." : result.message);
        StartCoroutine(EnterWorldBeforeLoading(selectedCharacter));
    }

    private IEnumerator EnterWorldBeforeLoading(CharacterData character)
    {
        isLoadingScene = true;
        selectRequestInFlight = false;
        RefreshButtonState();
        ShowMessage("Entering world...");
        if (loadingView != null)
            loadingView.Report(0.1f, "正在连接游戏服务器");

        if (!GateTransportBootstrap.EnsureTransport(out string transportError))
        {
            HandleGateEntryFailed(transportError);
            yield break;
        }

        gateEntry = new GateEntryCoordinator(
            NetworkManager.Instance, NetMessageDispatcher.Instance,
            character.characterDbId, character.characterId,
            gateEnterWorldTimeout,
            () => StartCoroutine(LoadSceneWithProgress(character.characterDbId)),
            HandleGateEntryFailed);
        gateEntry.Begin();
        while (gateEntry != null && !gateEntry.IsCompleted)
        {
            gateEntry.CheckTimeout();
            yield return null;
        }
    }

    private void HandleGateEntryFailed(string message)
    {
        Debug.LogError("[GateEntry] " + message);
        gateEntry?.Dispose();
        gateEntry = null;
        GateEntryHandoff.Clear();
        NetMessageDispatcher.Instance?.ClearDeferredWorldMessages();
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsConnected)
            NetworkManager.Instance.Disconnect();
        isLoadingScene = false;
        selectRequestInFlight = false;
        AuthSession auth = AuthSession.Instance;
        bool tokenRejected = message != null
            && (message.IndexOf("token is invalid or expired", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("token format is invalid", StringComparison.OrdinalIgnoreCase) >= 0);
        if (auth != null && auth.HasValidGameSession && !tokenRejected
            && long.TryParse(PlayerPrefs.GetString(SelectedCharacterDbIdKey, ""), out long retryCharacterDbId)
            && retryCharacterDbId > 0)
        {
            ShowEntryRecovery(message, retryCharacterDbId);
        }
        else
        {
            ReturnToLoginView(message, clearSession: true);
        }
        RefreshButtonState();
    }

    private void ShowSelectFailure(string message)
    {
        ShowMessage(message);
        if (loadingView != null)
        {
            loadingView.ShowFailure(message, () =>
            {
                loadingView.Hide();
                StartGame();
            }, loadingView.Hide);
        }
    }

    private IEnumerator LoadSceneWithProgress(long selectedCharacterDbId)
    {
        isLoadingScene = true;
        selectRequestInFlight = false;
        RefreshButtonState();

        const float sceneLoadStartProgress = 0.2f;
        if (loadingView != null)
            loadingView.Report(sceneLoadStartProgress, "正在加载游戏场景");

        NetworkManager network = NetworkManager.Instance;
        if (network == null || !network.IsConnected
            || !GateEntryHandoff.TryGetDestination(network.ConnectionEpoch,
                selectedCharacterDbId, out string serverSceneId, out string serverMapId)
            || !WorldMapSceneCatalog.TryGetAddress(serverSceneId, serverMapId, out _))
        {
            HandleGateEntryFailed("未收到有效的目标地图，或进场连接已失效。");
            yield break;
        }

        AsyncOperationHandle<SceneInstance> operation;
        try
        {
            operation = RemoteGameContent.LoadSceneAsync(successSceneName, false);
        }
        catch (Exception exception)
        {
            Debug.LogError("[GateEntry] World scene load failed: " + exception.Message);
            HandleGateEntryFailed("游戏场景资源加载失败，请检查资源连接后重试。");
            yield break;
        }
        while (!operation.IsDone)
        {
            if (loadingView != null)
            {
                float progress = Mathf.Lerp(sceneLoadStartProgress,
                    WorldEntryLoadingView.WorldSceneLoadedProgress, operation.PercentComplete);
                loadingView.Report(progress, "正在加载游戏场景");
            }
            yield return null;
        }
        if (operation.Status != AsyncOperationStatus.Succeeded)
        {
            string error = operation.OperationException == null
                ? successSceneName : operation.OperationException.Message;
            Addressables.Release(operation);
            Debug.LogError("[GateEntry] World scene load failed: " + error);
            HandleGateEntryFailed("游戏场景资源加载失败，请检查资源连接后重试。");
            yield break;
        }

        if (loadingView != null)
        {
            loadingView.Report(WorldEntryLoadingView.WorldSceneLoadedProgress, "正在切换游戏场景");
            loadingView.PersistForWorldEntry();
        }
        yield return operation.Result.ActivateAsync();
    }

    private void HandleRoleIndexChanged(int index)
    {
        ShowCurrentCharacterMessage();
        RefreshButtonState();
    }

    private void ShowCurrentCharacterMessage()
    {
        CharacterData character = CurrentCharacter;
        if (character == null)
        {
            if (nicknameText != null)
            {
                nicknameText.text = "";
            }

            return;
        }

        if (nicknameText != null)
        {
            nicknameText.text = character.nickname == null ? "" : character.nickname.Trim();
        }

        string displayName = string.IsNullOrWhiteSpace(character.characterName)
            ? character.characterId
            : character.characterName;
        ShowMessage(displayName + "  Lv." + Mathf.Max(1, character.level));
    }

    private Sprite FindCharacterSprite(CharacterData character)
    {
        if (character == null)
        {
            return unknownCharacterSprite;
        }

        Sprite sprite = FindCharacterSpriteById(character.characterId);
        if (sprite != null)
        {
            return sprite;
        }

        sprite = FindCharacterSpriteById(character.templateId);
        if (sprite != null)
        {
            return sprite;
        }

        sprite = FindCharacterSpriteById(character.classId);
        if (sprite != null)
        {
            return sprite;
        }

        sprite = FindCharacterSpriteById(character.appearanceId);
        return sprite != null ? sprite : unknownCharacterSprite;
    }

    private Sprite FindCharacterSpriteById(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            return null;
        }

        for (int index = 0; index < characterSprites.Count; index++)
        {
            CharacterSpriteMapping mapping = characterSprites[index];
            if (mapping != null && mapping.Matches(characterId))
            {
                return mapping.sprite != null ? mapping.sprite : unknownCharacterSprite;
            }
        }

        return null;
    }

    private int FindCharacterIndex(string characterId)
    {
        string savedCharacterDbId = PlayerPrefs.GetString(SelectedCharacterDbIdKey, "");
        if (long.TryParse(savedCharacterDbId, out long characterDbId) && characterDbId > 0)
        {
            int databaseIndex = characters.FindIndex(character =>
                character != null && character.characterDbId == characterDbId);
            if (databaseIndex >= 0)
            {
                return databaseIndex;
            }
        }

        if (string.IsNullOrWhiteSpace(characterId))
        {
            return -1;
        }

        return characters.FindIndex(character =>
            character != null && character.characterId == characterId);
    }

    private void ClearCharacterListView()
    {
        characters.Clear();
        roleSwitcher?.Clear();

        if (nicknameText != null)
        {
            nicknameText.text = "";
        }

        RefreshButtonState();
    }

    private void ClearSavedSelectedCharacter()
    {
        PlayerPrefs.DeleteKey(SelectedCharacterIdKey);
        PlayerPrefs.DeleteKey(SelectedCharacterDbIdKey);
        PlayerPrefs.DeleteKey(SelectedCharacterNameKey);
        PlayerPrefs.DeleteKey(SelectedCharacterTemplateKey);
        PlayerPrefs.Save();
    }

    private void RefreshButtonState()
    {
        bool busy = listRequestInFlight || selectRequestInFlight || createRequestInFlight || deleteRequestInFlight || isLoadingScene;
        bool hasCurrentCharacter = CurrentCharacter != null;

        if (startGameButton != null)
        {
            startGameButton.interactable = !busy && hasCurrentCharacter;
        }

        if (deleteCharacterButton != null)
        {
            deleteCharacterButton.interactable = !busy && hasCurrentCharacter;
        }

        if (openCreateCharacterButton != null)
        {
            openCreateCharacterButton.interactable = !busy && characters.Count < maxCharacterCount;
        }
    }

    private void ResolveReferences()
    {
        if (characterPanel == null)
        {
            characterPanel = gameObject;
        }

        if (createCharacterPanel == null)
        {
            createCharacterPanel = FindCreateCharacterPanel();
        }

        if (roleSwitcher == null)
        {
            roleSwitcher = FindListRoleSwitcher();
        }

        if (openCreateCharacterButton == null)
        {
            openCreateCharacterButton = FindButtonByName("CreatCharacter", "CreateCharacter", "OpenCreateCharacterButton");
        }

        if (startGameButton == null)
        {
            startGameButton = FindButtonByName("StartGame");
        }

        if (deleteCharacterButton == null)
        {
            deleteCharacterButton = FindButtonByName("DeleteCharacterButton", "DeleteCharacter");
        }

        if (messageText == null)
        {
            messageText = FindTextByName("ErrorMessageCreate", "MessageText");
        }

        if (nicknameText == null)
        {
            nicknameText = FindTextByName("NicknaneA", "NicknameA");
        }

        if (createCharacterPanelUI == null && createCharacterPanel != null)
        {
            createCharacterPanelUI = createCharacterPanel.GetComponent<CreateCharacterPanelUI>();
        }

        if (authApiClient == null)
        {
            authApiClient = FindSceneObject<AuthApiClient>();
        }
    }

    private void EnsureCreatePanelController()
    {
        if (createCharacterPanel == null)
        {
            createCharacterPanel = FindCreateCharacterPanel();
        }

        if (createCharacterPanel == null)
        {
            return;
        }

        if (createCharacterPanelUI == null)
        {
            createCharacterPanelUI = createCharacterPanel.GetComponent<CreateCharacterPanelUI>();
        }

        if (createCharacterPanelUI == null)
        {
            createCharacterPanelUI = createCharacterPanel.AddComponent<CreateCharacterPanelUI>();
        }

        createCharacterPanelUI.SetOwner(this);
    }

    private void SubscribeCreatePanel()
    {
        if (createCharacterPanelUI == null || createPanelEventSubscribed)
        {
            return;
        }

        createCharacterPanelUI.CreateRequested -= CreateCharacter;
        createCharacterPanelUI.CreateRequested += CreateCharacter;
        createPanelEventSubscribed = true;
    }

    private void UnsubscribeCreatePanel()
    {
        if (createCharacterPanelUI == null)
        {
            createPanelEventSubscribed = false;
            return;
        }

        createCharacterPanelUI.CreateRequested -= CreateCharacter;
        createPanelEventSubscribed = false;
    }

    private void EnsureSpriteMappings()
    {
        if (characterSprites.Count > 0)
        {
            return;
        }

        CharacterRoleSwitcher sourceSwitcher = FindSpriteSourceSwitcher();
        int count = Mathf.Max(maxCharacterCount, 3);
        for (int index = 0; index < count; index++)
        {
            Sprite sprite = sourceSwitcher != null && sourceSwitcher.Sprites != null && index < sourceSwitcher.Sprites.Count
                ? sourceSwitcher.Sprites[index]
                : unknownCharacterSprite;

            characterSprites.Add(new CharacterSpriteMapping
            {
                characterId = string.Format("character_{0:000}", index + 1),
                displayName = "Character " + (index + 1),
                sprite = sprite
            });
        }
    }

    private CharacterRoleSwitcher FindSpriteSourceSwitcher()
    {
        if (roleSwitcher != null && roleSwitcher.Sprites != null && roleSwitcher.Sprites.Count > 0)
        {
            return roleSwitcher;
        }

        CharacterRoleSwitcher[] switchers = GetComponentsInChildren<CharacterRoleSwitcher>(true);
        for (int index = 0; index < switchers.Length; index++)
        {
            CharacterRoleSwitcher switcher = switchers[index];
            if (switcher != null && switcher.Sprites != null && switcher.Sprites.Count > 0)
            {
                return switcher;
            }
        }

        return null;
    }

    private CharacterRoleSwitcher FindListRoleSwitcher()
    {
        CharacterRoleSwitcher[] switchers = GetComponentsInChildren<CharacterRoleSwitcher>(true);
        for (int index = 0; index < switchers.Length; index++)
        {
            CharacterRoleSwitcher switcher = switchers[index];
            if (switcher == null)
            {
                continue;
            }

            if (createCharacterPanel != null && switcher.transform.IsChildOf(createCharacterPanel.transform))
            {
                continue;
            }

            return switcher;
        }

        return null;
    }

    private GameObject FindCreateCharacterPanel()
    {
        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < transforms.Length; index++)
        {
            Transform item = transforms[index];
            if (item == null || item == transform)
            {
                continue;
            }

            string itemName = item.name.Trim();
            if (string.Equals(itemName, "CreateCharacterPanel", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(itemName, "CreatCharacterPanel", StringComparison.OrdinalIgnoreCase))
            {
                return item.gameObject;
            }
        }

        for (int index = 0; index < transforms.Length; index++)
        {
            Transform item = transforms[index];
            if (item == null || item == transform)
            {
                continue;
            }

            Button submitButtonCandidate = FindButtonInChildren(item, "submitButton", "SubmitButton");
            if (submitButtonCandidate != null)
            {
                if (submitButtonCandidate.transform == item && item.parent != null)
                {
                    return item.parent.gameObject;
                }

                return item.gameObject;
            }
        }

        return null;
    }

    private Button FindButtonByName(params string[] names)
    {
        return FindButtonInChildren(transform, names);
    }

    private Button FindButtonInChildren(Transform root, params string[] names)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int buttonIndex = 0; buttonIndex < buttons.Length; buttonIndex++)
        {
            Button button = buttons[buttonIndex];
            for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
            {
                if (string.Equals(button.name.Trim(), names[nameIndex], StringComparison.OrdinalIgnoreCase))
                {
                    return button;
                }
            }
        }

        return null;
    }

    private TMP_Text FindTextByName(params string[] names)
    {
        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
        for (int textIndex = 0; textIndex < texts.Length; textIndex++)
        {
            TMP_Text text = texts[textIndex];
            for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
            {
                if (string.Equals(text.name.Trim(), names[nameIndex], StringComparison.OrdinalIgnoreCase))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static T FindSceneObject<T>() where T : Component
    {
        foreach (T item in Resources.FindObjectsOfTypeAll<T>())
        {
            if (IsSceneObject(item))
            {
                return item;
            }
        }

        return null;
    }

    private static bool IsSceneObject(Component item)
    {
        return item != null && item.gameObject.scene.IsValid() && item.hideFlags == HideFlags.None;
    }

    private bool TryGetSession(out AuthSession session)
    {
        session = AuthSession.Instance;
        return session != null && session.IsLoggedIn &&
               !string.IsNullOrWhiteSpace(session.UserId);
    }

    private CharacterData GetValidSelectedCharacter(AuthResult result, CharacterData fallbackCharacter)
    {
        CharacterData selectedCharacter = result == null ? null : result.character;
        if (selectedCharacter == null || string.IsNullOrWhiteSpace(selectedCharacter.characterId))
        {
            if (selectedCharacter == null)
            {
                selectedCharacter = new CharacterData();
            }

            if (result != null && !string.IsNullOrWhiteSpace(result.characterId))
            {
                selectedCharacter.characterId = result.characterId;
            }
            else if (fallbackCharacter != null && !string.IsNullOrWhiteSpace(fallbackCharacter.characterId))
            {
                selectedCharacter.characterId = fallbackCharacter.characterId;
            }
        }

        if (selectedCharacter.characterDbId <= 0)
        {
            if (result != null && result.characterDbId > 0)
            {
                selectedCharacter.characterDbId = result.characterDbId;
            }
            else if (fallbackCharacter != null)
            {
                selectedCharacter.characterDbId = fallbackCharacter.characterDbId;
            }
        }

        if (fallbackCharacter != null)
        {
            if (string.IsNullOrWhiteSpace(selectedCharacter.characterName))
            {
                selectedCharacter.characterName = fallbackCharacter.characterName;
            }

            if (string.IsNullOrWhiteSpace(selectedCharacter.templateId))
            {
                selectedCharacter.templateId = fallbackCharacter.templateId;
            }
        }

        return selectedCharacter;
    }

    private bool SaveSelectedCharacter(CharacterData character)
    {
        if (character == null || character.characterDbId <= 0)
        {
            Debug.LogWarning("Save selected character skipped: characterDbId is missing or invalid.");
            return false;
        }

        PlayerPrefs.SetString(SelectedCharacterDbIdKey, character.characterDbId.ToString());
        PlayerPrefs.SetString(SelectedCharacterIdKey, character.characterId);
        PlayerPrefs.SetString(SelectedCharacterNameKey, character.characterName ?? "");
        PlayerPrefs.SetString(SelectedCharacterTemplateKey, character.templateId ?? "");
        PlayerPrefs.Save();
        Debug.Log("Saved selected character: characterDbId=" + character.characterDbId + ", characterId=" + character.characterId);
        return true;
    }

    private void ShowMessage(string message)
    {
        if (messageText != null)
        {
            messageText.text = message ?? "";
        }

        Debug.Log("CharacterSelectPanelUI: " + message);
    }
}

[Serializable]
public class CharacterData
{
    public long characterDbId;
    public string characterId;
    public string characterName;
    public string nickname;
    public string templateId;
    public string classId;
    public string appearanceId;
    public int level;
    public string sceneName;
}

[Serializable]
public class CharacterListRequest
{
    public string channel;
    public string userId;
    public string username;
}

[Serializable]
public class CharacterListResult
{
    public bool success;
    public string message;
    public CharacterData[] characters;
}

[Serializable]
public class CharacterCreateRequest
{
    public string channel;
    public string characterId;
    public string roleId;
    public string userId;
    public string nickname;
}

[Serializable]
public class CharacterCreateResult
{
    public bool success;
    public string message;
    public string characterId;
    public CharacterData character;
}

[Serializable]
public class CharacterDeleteRequest
{
    public string channel;
    public long characterDbId;
    public string characterId;
    public string userId;
    public bool confirm;
}

[Serializable]
public class CharacterDeleteResult
{
    public bool success;
    public string message;
    public string characterId;
}

[Serializable]
public class CharacterSelectRequest
{
    public string channel;
    public long characterDbId;
    public string characterId;
    public string userId;
    public string username;
}

[Serializable]
public class CharacterSelectResult
{
    public bool success;
    public string message;
    public CharacterData character;
}
