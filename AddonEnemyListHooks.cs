using System;
using System.Diagnostics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace EnemyListDebuffs
{
    // 契約：兩個事件都由 Dalamud 在框架執行緒、遊戲呼叫原函式「之前」觸發；addon 指標只在該次呼叫內有效，不留存。
    public unsafe class AddonEnemyListHooks : IDisposable
    {
        public const string AddonName = "_EnemyList";

        private readonly EnemyListDebuffsPlugin _plugin;

        private readonly Stopwatch _timer;
        private long _elapsed;

        public AddonEnemyListHooks(EnemyListDebuffsPlugin p)
        {
            _plugin = p;

            _timer = new Stopwatch();
            _elapsed = 0;
        }

        public void Initialize()
        {
            _plugin.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, AddonName, OnPreDraw);
            _plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, OnPreFinalize);
        }

        public void Dispose()
        {
            _plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, AddonName, OnPreDraw);
            _plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, AddonName, OnPreFinalize);
        }

        private void OnPreFinalize(AddonEvent type, AddonArgs args)
        {
            _plugin.StatusNodeManager.DestroyNodes((AddonEnemyList*)args.Addon.Address);
        }

        private void OnPreDraw(AddonEvent type, AddonArgs args)
        {
            var thisPtr = (AddonEnemyList*)args.Addon.Address;
            if (thisPtr == null)
                return;

            if (!_plugin.Config.Enabled || _plugin.InPvp)
            {
                if (_timer.IsRunning)
                {
                    _timer.Stop();
                    _timer.Reset();
                    _elapsed = 0;
                }

                if (_plugin.StatusNodeManager.Built)
                    _plugin.StatusNodeManager.DestroyNodes(thisPtr);

                return;
            }

            _elapsed += _timer.ElapsedMilliseconds;
            _timer.Restart();

            if (_elapsed < _plugin.Config.UpdateInterval)
                return;

            // 半重建期（進出副本／切區）節點樹不完整；AVE 攔不到，只能在解參考之前判斷，下一幀再做。
            if (!IsAddonReady(thisPtr))
                return;

            if (!_plugin.StatusNodeManager.IsBuiltOn(thisPtr))
            {
                // 節點掛在別的視窗實例上＝沒收到那個實例的關閉事件；舊節點不再碰。
                if (_plugin.StatusNodeManager.Built)
                    _plugin.StatusNodeManager.ForgetNodes("敵對列表視窗已換了一個實例");

                if (!_plugin.StatusNodeManager.BuildNodes(thisPtr))
                    return;
            }

            // AtkStage.Instance() 是解參考的靜態指標，可能為 null。
            var atkStage = AtkStage.Instance();
            if (atkStage == null)
                return;

            var numArray = atkStage->GetNumberArrayData(NumberArrayType.EnemyList);

            for (var i = 0; i < thisPtr->EnemyCount; i++)
                if (_plugin.UI.IsConfigOpen)
                {
                    _plugin.StatusNodeManager.ForEachNode(node =>
                        node.SetStatus(StatusNode.StatusNode.DefaultIconId, 20));
                }
                else
                {
                    var localPlayerId = _plugin.ObjectTable.LocalPlayer?.GameObjectId;
                    if (localPlayerId is null)
                    {
                        _plugin.StatusNodeManager.HideUnusedStatus(i, 0);
                        continue;
                    }

                    // GetNumberArrayData 在陣列尚未建立時回 null；CharacterManager.Instance() 不是指標型靜態位址、永不 null。
                    if (numArray == null)
                    {
                        _plugin.StatusNodeManager.HideUnusedStatus(i, 0);
                        continue;
                    }

                    var enemyObjectId = numArray->IntArray[8 + i * 6];
                    var enemyChara = CharacterManager.Instance()->LookupBattleCharaByEntityId((uint)enemyObjectId);

                    if (enemyChara is null) continue;

                    var targetStatus = enemyChara->GetStatusManager();

                    var statusArray = targetStatus->Status;

                    var count = 0;

                    for (var j = 0; j < 30; j++)
                    {
                        Status status = statusArray[j];
                        if (status.StatusId == 0) continue;
                        if (status.SourceObject.ObjectId != localPlayerId) continue;

                        _plugin.StatusNodeManager.SetStatus(i, count, status.StatusId, (int)status.RemainingTime);
                        count++;

                        if (count == 4)
                            break;
                    }

                    _plugin.StatusNodeManager.HideUnusedStatus(i, count);
                }

            _elapsed = 0;
        }

        // 已顯示、根節點已建立、ULD 已載入完成，三者皆真才可以碰節點。
        private static bool IsAddonReady(AddonEnemyList* addon)
        {
            if (addon == null)
                return false;
            if (!addon->AtkUnitBase.IsVisible)
                return false;
            if (addon->AtkUnitBase.RootNode == null)
                return false;
            if (addon->AtkUnitBase.UldManager.LoadedState != AtkLoadState.Loaded)
                return false;
            return true;
        }
    }
}
