using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Voxif.AutoSplitter;
using Voxif.Helpers.Unity;
using Voxif.IO;
using Voxif.Memory;

namespace LiveSplit.DeathsDoor {
    public class DeathsDoorMemory : Memory {

        protected override string[] ProcessNames => new string[] { "DeathsDoor" };

        public Pointer<bool> IsCurrentlyLoading { get; private set; }
        public StringPointer Scene { get; private set; }

        public Pointer<bool> LoadingIconShown { get; private set; }

        private Pointer<IntPtr> TitleScreen { get; set; }
        private Pointer<bool> OptionsPanelEnabled { get; set; }
        private Pointer<int> TitleScreenIndex { get; set; }
        private Pointer<IntPtr> SaveSlots { get; set; }
        private Pointer<IntPtr> SlotTransition { get; set; }
        private Pointer<int> SlotIndex { get; set; }

        private StringPointer SpawnId { get; set; }
        
        private Pointer<IntPtr> BrainList { get; set; }

        private Pointer<Vector3> PlayerPosition { get; set; }

        private readonly DictData<int> countKeys = new DictData<int>();
        private readonly DictData<bool> boolKeys = new DictData<bool>();

        private readonly HashSet<IntPtr> aiBrains = new HashSet<IntPtr>();
        private int aiBrainVersion;

        private readonly float[] gameTimes = new float[3];

        // Personalized splits
        private readonly bool[] gameNightStates = new bool[3];
        //private readonly float[] gamePercentages = new float[3];

        private bool saveIsInitialized = false;

        private UnityHelperTask unityTask;

        public DeathsDoorMemory(Logger logger) : base(logger) {
            OnHook += () => {
                unityTask = new UnityHelperTask(game, logger);
                unityTask.Run(InitPointers);
            };

            OnExit += () => {
                if(unityTask != null) {
                    unityTask.Dispose();
                    unityTask = null;
                }
            };
        }

        private void InitPointers(IMonoHelper unity) {
            MonoNestedPointerFactory ptrFactory = new MonoNestedPointerFactory(game, unity);

            var gameSceneManager = ptrFactory.Make("GameSceneManager");
            IsCurrentlyLoading = ptrFactory.Make<bool>(gameSceneManager, "instance", "isCurrentlyLoading");
            Scene = ptrFactory.MakeString(gameSceneManager, "currentScene", ptrFactory.StringHeaderSize);
            Scene.StringType = EStringType.UTF16Sized;

            LoadingIconShown = ptrFactory.Make<bool>("LoadingIcon", "instance", "show");

            TitleScreen = ptrFactory.Make<IntPtr>("TitleScreen", "instance", out IntPtr titleScreenClass);
            OptionsPanelEnabled = ptrFactory.Make<bool>(TitleScreen, unity.GetFieldOffset(titleScreenClass, "optionsPanel"), 0x10, 0x39);
            TitleScreenIndex = ptrFactory.Make<int>(TitleScreen, unity.GetFieldOffset(titleScreenClass, "index"));
            var saveMenu = ptrFactory.Make<IntPtr>(TitleScreen, unity.GetFieldOffset(titleScreenClass, "saveMenu"));
            {
                IntPtr saveMenuClass = unity.FindClass("SaveMenu");
                SaveSlots = ptrFactory.Make<IntPtr>(saveMenu, unity.GetFieldOffset(saveMenuClass, "saveSlots"));
                SlotTransition = ptrFactory.Make<IntPtr>(saveMenu, unity.GetFieldOffset(saveMenuClass, "transitionButton"));
                SlotIndex = ptrFactory.Make<int>(saveMenu, unity.GetFieldOffset(saveMenuClass, "index"));
            }

            var gameSave = ptrFactory.Make<IntPtr>("GameSave", "currentSave", out IntPtr gameSaveClass);
            SpawnId = ptrFactory.MakeString(gameSave, unity.GetFieldOffset(gameSaveClass, "spawnId"), ptrFactory.StringHeaderSize);
            SpawnId.StringType = EStringType.UTF16Sized;
            countKeys.pointer = ptrFactory.Make<IntPtr>(gameSave, unity.GetFieldOffset(gameSaveClass, "countKeys"));
            boolKeys.pointer = ptrFactory.Make<IntPtr>(gameSave, unity.GetFieldOffset(gameSaveClass, "boolKeys"));
            
            BrainList = ptrFactory.Make<IntPtr>("AI_Brain", "brainList");
        
            PlayerPosition = ptrFactory.Make<Vector3>("PlayerGlobal", "instance", 0x10, 0x30, 0x30, 0x8, 0x28, 0x10, 0x38, 0x180);

            logger.Log(ptrFactory.ToString());

            unityTask = null;
        }

        public override bool Update() {
            if(base.Update() && unityTask == null) {
                if(!saveIsInitialized && SpawnId.New.Equals("bus_overridespawn", StringComparison.Ordinal)) {
                    saveIsInitialized = true;
                }
                return true;
            }
            return false;
        }

        public void ResetData() {
            saveIsInitialized = false;
            boolKeys.Clear();
            countKeys.Clear();
        }

        public bool HasStartedANewSave() {
            return SlotTransition.New != default && GameTimeOfSlot(SlotIndex.New) == 0;
        }

        public bool HasDeletedASave() {
            if(SaveSlots.New == default) {
                return false;
            }
            for(int slotId = 0; slotId < gameTimes.Length; slotId++) {
                float time = GameTimeOfSlot(slotId);
                if(time != gameTimes[slotId]) {
                    float oldTime = gameTimes[slotId];
                    gameTimes[slotId] = time;
                    if(time == 0 && oldTime != 0) {
                        return true;
                    }
                }
            }
            return false;
        }

        private float GameTimeOfSlot(int index) {
            return game.Read<float>(SaveSlots.New, 0x20 + 0x8 * index, 0x18, 0x18, 0x40);
        }

        //private float GamePercentageOfSlot(int index) {
        //    string text = game.ReadString(game.Read<IntPtr>(SaveSlots.New, 0x20 + 0x8 * index, 0x38, 0xb0), EStringType.UTF16Sized);
        //    return float.Parse(text);
        //}

        private void UpdateAIBrains(IEnumerable<string> aiBrainsToCheck) {
            int version = game.Read<int>(BrainList.New + 0x1C);
            if(version == aiBrainVersion) {
                return;
            }
            aiBrainVersion = version;

            aiBrains.Clear();
            int count = game.Read<int>(BrainList.New + 0x18);
            if(count == 0) {
                return;
            }
            IntPtr items = game.Read<IntPtr>(BrainList.New + 0x10);
            for(int id = 0; id < count; id++) {
                IntPtr aiBrain = game.Read<IntPtr>(items + 0x20 + 0x8 * id);
                string aiBrainName = GetGameObjectName(aiBrain);
                if(aiBrainsToCheck.Contains(aiBrainName)) {
                    aiBrains.Add(aiBrain);
                }
            }
        }

        public IEnumerable<string> NewAIBrainDeadSequence(HashSet<string> aiBrainsToCheck) {
            UpdateAIBrains(aiBrainsToCheck);

            if(aiBrains.Count == 0) {
                yield break;
            }

            HashSet<IntPtr> aiBrainsCopy = new HashSet<IntPtr>(aiBrains);
            foreach(IntPtr aiBrain in aiBrainsCopy) {
                float health = game.Read<float>(aiBrain, 0x58, 0x48);
                if(health <= 0) {
                    aiBrains.Remove(aiBrain);
                    string name = GetGameObjectName(aiBrain);
                    aiBrainsToCheck.Remove(name);
                    yield return name;
                }
            }
        }

        private string GetGameObjectName(IntPtr ptr) {
            return game.ReadString(game.Read(ptr, 0x10, 0x30, 0x60, 0x0), EStringType.UTF8);
        }

        public IEnumerable<string> NewBoolSequence() {
            foreach(KeyValuePair<string, bool> kvp in UpdateDict(boolKeys)) {
                if(kvp.Value) {
                    yield return kvp.Key;
                }
            }
        }

        private bool NightStateOfSlot(int index) {
            return game.Read<bool>(SaveSlots.New, 0x20 + 0x8 * index, 0x18, 0x18, 0x44);
        }

        public bool NightChanged() {
            if(SaveSlots.New == default) {
                return false;
            }
            for(int slotId = 0; slotId < gameNightStates.Length; slotId++) {
                bool isNight = NightStateOfSlot(slotId);
                if(isNight != gameNightStates[slotId]) {
                    bool oldNightState = gameNightStates[slotId];
                    gameNightStates[slotId] = isNight;
                    if(isNight && !oldNightState) {
                        return true;
                    }
                }
            }
            return false;
        }

        public IEnumerable<string> NewCountSequence() {
            foreach(KeyValuePair<string, int> kvp in UpdateDict(countKeys)) {
                yield return kvp.Key + "_" + kvp.Value;
            }
        }

        private IEnumerable<KeyValuePair<string, T>> UpdateDict<T>(DictData<T> dictData) where T : unmanaged {
            if(!saveIsInitialized) {
                yield break;
            }

            int version = game.Read<int>(dictData.pointer.New + 0x44);
            if(version == dictData.version) {
                yield break;
            }
            dictData.version = version;

            IntPtr entries = game.Read<IntPtr>(dictData.pointer.New + 0x18);
            int count = game.Read<int>(dictData.pointer.New + 0x40);
            for(int i = 0; i < count; i++) {
                IntPtr entryOffset = entries + 0x20 + 0x18 * i;
                string key = game.ReadString(game.Read(entryOffset, 0x8, 0x14), EStringType.UTF16Sized);
                T value = game.Read<T>(entryOffset + 0x10);
                if(!dictData.dict.ContainsKey(key)) {
#if DEBUG
                    Debug($"Dict add {key}: {value}");
#endif
                    dictData.dict.Add(key, value);
                    yield return new KeyValuePair<string, T>(key, value);
                } else if(!dictData.dict[key].Equals(value)) {
#if DEBUG                    
                    Debug($"Dict change {key}: {dictData.dict[key]} -> {value}");
#endif
                    dictData.dict[key] = value;
                    yield return new KeyValuePair<string, T>(key, value);
                }
            }

#if DEBUG
            void Debug(string msg) {
                Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " --- " + msg);
            }
#endif
        }

        public bool IsInTruthTrigger() {
            const float x = -128.9004f;
            const float width = 6.1308f;
            const float z = 789.7526f;
            const float depth = 43.6074f;

            const float pSize = 1f;

            return Scene.New.Equals("lvlConnect_Fortress_Mountaintops", StringComparison.Ordinal)
                && PlayerPosition.New.x - pSize < x + width && PlayerPosition.New.x + pSize > x - width
                && PlayerPosition.New.z - pSize < z + depth && PlayerPosition.New.z + pSize > z - depth;
        }
        
        public bool LoadingTitleScreen() {
            return Scene.New.Equals("TitleScreen", StringComparison.Ordinal)
                && (TitleScreen.New == default || (OptionsPanelEnabled.New && TitleScreenIndex.New == 0));
        }

        private class DictData<T> where T : unmanaged {
            public Dictionary<string, T> dict = new Dictionary<string, T>();
            public Pointer<IntPtr> pointer = null;
            public int version = default;

            public void Clear() {
                dict.Clear();
                version = 0;
            }
        }
    }

    public struct Vector3 {
        public float x, y, z;

        public Vector3(float x, float y, float z) {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }
}