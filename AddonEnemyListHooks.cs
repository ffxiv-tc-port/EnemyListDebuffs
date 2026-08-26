using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Dalamud;
using Dalamud.Hooking;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace EnemyListDebuffs
{
    public unsafe class AddonEnemyListHooks : IDisposable
    {
        private readonly int _drawVtblOffset = 44 * IntPtr.Size;
        private readonly EnemyListDebuffsPlugin _plugin;

        private readonly Stopwatch _timer;
        private long _elapsed;
        private Hook<AddonEnemyList.Delegates.Finalizer> _hookAddonEnemyListFinalize;

        private AddonEnemyList.Delegates.Draw _origDrawFunc;

        private IntPtr _origEnemyListDrawFuncPtr;
        private AddonEnemyList.Delegates.Draw _replaceDrawFunc;

        public AddonEnemyListHooks(EnemyListDebuffsPlugin p)
        {
            _plugin = p;

            _timer = new Stopwatch();
            _elapsed = 0;
        }

        public void Dispose()
        {
            _hookAddonEnemyListFinalize.Dispose();
            var vtblFuncAddr = _plugin.Address.AddonEnemyListVTBLAddress + _drawVtblOffset;
            MemoryHelper.ChangePermission(vtblFuncAddr, 8, MemoryProtection.ReadWrite, out var oldProtect);
            SafeMemory.Write(_plugin.Address.AddonEnemyListVTBLAddress + _drawVtblOffset, _origEnemyListDrawFuncPtr);
            MemoryHelper.ChangePermission(vtblFuncAddr, 8, oldProtect, out oldProtect);
        }
        
        public void Initialize()
        {
            _hookAddonEnemyListFinalize = _plugin.GameInteropProvider.HookFromAddress<AddonEnemyList.Delegates.Finalizer>
                (_plugin.Address.AddonEnemyListFinalizeAddress, AddonEnemyListFinalizeDetour);
            
            _origEnemyListDrawFuncPtr = Marshal.ReadIntPtr(_plugin.Address.AddonEnemyListVTBLAddress, _drawVtblOffset);
            _origDrawFunc = Marshal.GetDelegateForFunctionPointer<AddonEnemyList.Delegates.Draw>(_origEnemyListDrawFuncPtr);

            _plugin.PluginLog.Debug($"{_origEnemyListDrawFuncPtr.ToInt64():X}");

            _replaceDrawFunc = AddonEnemyListDrawDetour;
            var replaceDrawFuncPtr = Marshal.GetFunctionPointerForDelegate(_replaceDrawFunc);

            var vtblFuncAddr = _plugin.Address.AddonEnemyListVTBLAddress + _drawVtblOffset;
            MemoryHelper.ChangePermission(vtblFuncAddr, 8, MemoryProtection.ReadWrite, out var oldProtect);
            SafeMemory.Write(vtblFuncAddr, replaceDrawFuncPtr);
            MemoryHelper.ChangePermission(vtblFuncAddr, 8, oldProtect, out oldProtect);

            _hookAddonEnemyListFinalize.Enable();
        }

        public void AddonEnemyListDrawDetour(AddonEnemyList* thisPtr)
        {
            if (!_plugin.Config.Enabled || _plugin.InPvp)
            {
                if (_timer.IsRunning)
                {
                    _timer.Stop();
                    _timer.Reset();
                    _elapsed = 0;
                }

                if (_plugin.StatusNodeManager.Built)
                {
                    _plugin.StatusNodeManager.DestroyNodes();
                    _plugin.StatusNodeManager.SetEnemyListAddonPointer(null);
                }

                _origDrawFunc(thisPtr);
                return;
            }

            _elapsed += _timer.ElapsedMilliseconds;
            _timer.Restart();

            if (_elapsed >= _plugin.Config.UpdateInterval)
            {
                // 台服加固：addon-ready 閘門（比照 Saucy 卡片視窗 AVE 修法）。
                // 對 EnemyOneComponent 做節點注入／操作前，先確認 addon 處於可安全操作狀態：
                // 進出副本／切 zone 的半重建期，IsVisible / RootNode / UldManager.LoadedState 不會全真。
                // 不滿足就本幀安全跳過節點操作（呼叫原始 Draw 後早退），下一幀 ready 再做。
                // 🔴 這是原生指針解參考「之前」的守衛；try/catch 對 AccessViolation 無效，只能靠前置判斷避開。
                if (!IsAddonReady(thisPtr))
                {
                    _origDrawFunc(thisPtr);
                    return;
                }

                if (!_plugin.StatusNodeManager.Built)
                {
                    _plugin.StatusNodeManager.SetEnemyListAddonPointer(thisPtr);
                    if (!_plugin.StatusNodeManager.BuildNodes())
                        return;
                }

                // 台服加固：AtkStage.Instance() 是 [StaticAddress(..., isPointer:true)]，
                // 屬「解參考靜態指標」語意、可能為 null（非 lea 取本身位址的 A 類），故判空是必要而非死碼。
                var atkStage = AtkStage.Instance();
                if (atkStage == null)
                {
                    _origDrawFunc(thisPtr);
                    return;
                }

                var numArray = atkStage->GetNumberArrayData(NumberArrayType.EnemyList);

                // var numArray = Framework.Instance()->GetUIModule()->GetRaptureAtkModule()->AtkModule.AtkArrayDataHolder
                //     .NumberArrays[21];

                for (var i = 0; i < thisPtr->EnemyCount; i++)
                    if (_plugin.UI.IsConfigOpen)
                    {
                        _plugin.StatusNodeManager.ForEachNode(node =>
                            node.SetStatus(StatusNode.StatusNode.DefaultIconId, 20));
                    }
                    else
                    {
                        // API13：IClientState.LocalPlayer 已過時，改用 IObjectTable.LocalPlayer（純轉發）。
                        var localPlayerId = _plugin.ObjectTable.LocalPlayer?.GameObjectId;
                        if (localPlayerId is null)
                        {
                            _plugin.StatusNodeManager.HideUnusedStatus(i, 0);
                            continue;
                        }

                        // 台服加固：GetNumberArrayData 可能回 null（該陣列尚未建立）；
                        // 此處早退並隱藏本列狀態，避免對 null 陣列取索引造成 AVE。
                        // 注意：CharacterManager.Instance() 是 [StaticAddress]（無 isPointer:true）＝A 類永不 null，故不判空（判了是死碼）。
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

            _origDrawFunc(thisPtr);
        }

        public void AddonEnemyListFinalizeDetour(AddonEnemyList* thisPtr)
        {
            _plugin.StatusNodeManager.DestroyNodes();
            _plugin.StatusNodeManager.SetEnemyListAddonPointer(null);
            _hookAddonEnemyListFinalize.Original(thisPtr);
        }

        // 台服加固：addon 是否處於「可安全操作原生節點」的狀態。
        // 半重建期（進出副本／切 zone、addon 尚在 setup／teardown）這三項不會全真：
        //   IsVisible                       —— 已顯示
        //   RootNode != null                —— 根節點已建立
        //   UldManager.LoadedState==Loaded  —— ULD 資源已完整載入（Loaded=3）
        // 三者皆真才回 true。回 false 時呼叫端本幀跳過節點操作，不解參考任何原生指標。
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