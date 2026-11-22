using TMPro;
using UnityEngine;

namespace BreakMyRoom
{
    public sealed class RoomGameFlow : MonoBehaviour
    {
        public enum GameState
        {
            Title,
            Playing,
            Results
        }

        [Header("Screens")]
        [SerializeField] private GameObject titleScreen;
        [SerializeField] private GameObject resultScreen;
        [SerializeField] private GameObject playControls;
        [SerializeField] private GameObject roomInstructions;
        [SerializeField] private GameObject toolHud;

        [Header("Results")]
        [SerializeField] private TextMeshProUGUI resultText;

        [Header("Room")]
        [SerializeField] private ToolDock[] tools;
        [SerializeField] private Breakable[] breakables;
        [SerializeField] private PaintMarkPool[] markPools;

        private float startedAt;
        public GameState State { get; private set; }
        public float ElapsedSeconds { get; private set; }

        public void Configure(GameObject title, GameObject results, GameObject controls, GameObject instructions,
            GameObject hud, TextMeshProUGUI summary, ToolDock[] roomTools, Breakable[] roomBreakables, PaintMarkPool[] pools)
        {
            titleScreen = title;
            resultScreen = results;
            playControls = controls;
            roomInstructions = instructions;
            toolHud = hud;
            resultText = summary;
            tools = roomTools;
            breakables = roomBreakables;
            markPools = pools;
        }

        private void Start()
        {
            ShowTitle();
        }

        public void StartGame()
        {
            if (State == GameState.Playing) return;

            SetToolsActive(false);
            RestoreRoom();
            foreach (PaintMarkPool pool in markPools)
                pool.ClearMarks();
            ElapsedSeconds = 0f;
            startedAt = Time.time;
            State = GameState.Playing;
            ShowScreens();
            SetToolsActive(true);
        }

        public void EndGame()
        {
            if (State != GameState.Playing) return;

            ElapsedSeconds = Time.time - startedAt;
            int wallCount = 0;
            int destroyedWalls = 0;
            int furnitureCount = 0;
            int destroyedFurniture = 0;
            foreach (Breakable breakable in breakables)
            {
                bool isWall = breakable.Kind == Breakable.BreakableKind.Wall;
                if (isWall) wallCount++;
                else furnitureCount++;

                if (breakable.IsDestroyed)
                {
                    if (isWall) destroyedWalls++;
                    else destroyedFurniture++;
                }
                breakable.HideDetachedPieces();
            }
            int marks = 0;
            foreach (PaintMarkPool pool in markPools)
                marks += pool.ActiveMarkCount;
            int seconds = Mathf.FloorToInt(ElapsedSeconds);
            resultText.text = $"플레이 시간  {seconds / 60:00}:{seconds % 60:00}\n부순 벽·바닥·천장  {destroyedWalls} / {wallCount}칸\n부순 가구  {destroyedFurniture} / {furnitureCount}개\n남은 자국  {marks}개";
            State = GameState.Results;
            SetToolsActive(false);
            ShowScreens();
        }

        // 방 복구 버튼·새 판 시작 공용
        public void RestoreRoom()
        {
            foreach (Breakable breakable in breakables)
                breakable.ResetBreakable();
        }

        public void ShowTitle()
        {
            if (State == GameState.Playing) return;

            State = GameState.Title;
            SetToolsActive(false);
            ShowScreens();
        }

        private void SetToolsActive(bool active)
        {
            foreach (ToolDock tool in tools)
            {
                // 비활성화 시 XRI 선택 해제 후 HUD 슬롯으로 복귀
                if (active == false)
                {
                    tool.gameObject.SetActive(false);
                    tool.ReturnToDock();
                }
                else
                {
                    tool.ReturnToDock();
                    tool.gameObject.SetActive(true);
                }
            }
        }

        private void ShowScreens()
        {
            bool playing = State == GameState.Playing;
            titleScreen.SetActive(State == GameState.Title);
            resultScreen.SetActive(State == GameState.Results);
            playControls.SetActive(playing);
            roomInstructions.SetActive(playing);
            toolHud.SetActive(playing);
        }
    }
}
