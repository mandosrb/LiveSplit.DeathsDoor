using System.Collections.Generic;
using System.Linq;

namespace LiveSplit.DeathsDoor {
    public partial class DeathsDoorComponent {

        private readonly RemainingDictionary remainingSplits;
        private readonly HashSet<string> aiBrainsToCheck = new HashSet<string>();

        public override bool Update() {
            return memory.Update();
        }

        public override bool Start() {
            return memory.HasStartedANewSave();
        }

        public override void OnStart() {
            HashSet<string> splits = new HashSet<string>(settings.Splits);

            aiBrainsToCheck.Clear();
            foreach(string split in settings.Splits) {
                if(split.StartsWith("AIBrain_")) {
                    aiBrainsToCheck.Add(split.Substring(8));
                    splits.Remove(split);
                }
            }

            remainingSplits.Setup(splits);
            memory.ResetData();
        }

        public override bool Split() {
            return (remainingSplits.Count() != 0 && (SplitBool() || SplitScene() || SplitTruthEnding())) || SplitAIBrain();

            bool SplitAIBrain() {
                if(aiBrainsToCheck.Count == 0) {
                    return false;
                }
                foreach(string name in memory.NewAIBrainDeadSequence(aiBrainsToCheck)) {
                    logger.Log("Split AIBrain, " + name);
                    return true;
                }
                return false;
            }

            bool SplitBool() {
                if(!remainingSplits.ContainsKey("Bool")) {
                    return false;
                }
                foreach(string name in memory.NewBoolSequence()) {
                    if(remainingSplits.Split("Bool", name)) {
                        return true;
                    }
                }
                if(memory.NightChanged()) {
                    return true;
                }
                return false;
            }

            bool SplitScene() {
                return remainingSplits.ContainsKey("Scene")
                    && memory.Scene.Changed
                    && remainingSplits.Split("Scene", memory.Scene.New);
            }

            bool SplitTruthEnding() {
                return remainingSplits.ContainsKey("TruthEnding")
                    && memory.IsInTruthTrigger()
                    && remainingSplits.Split("TruthEnding");
            }

        }

        public override bool Reset() {
            return memory.HasDeletedASave();
        }

        public override bool Loading() {
            return memory.LoadingIconShown.New || memory.IsCurrentlyLoading.New || memory.LoadingTitleScreen();
        }
    }
}