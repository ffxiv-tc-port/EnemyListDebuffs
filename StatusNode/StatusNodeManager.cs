using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System;

namespace EnemyListDebuffs.StatusNode
{
    public unsafe class StatusNodeManager : IDisposable
    {
        private EnemyListDebuffsPlugin _plugin;

        // 只拿來比對「是不是建立節點時那個視窗實例」，永不解參考。
        private nint _builtOn;

        private StatusNodeGroup[] NodeGroups;

        private ExcelSheet<Status> StatusSheet;

        private static uint StartingNodeId = 50000;

        public bool Built { get; private set; }

        internal StatusNodeManager(EnemyListDebuffsPlugin p)
        {
            _plugin = p; 

            NodeGroups = new StatusNodeGroup[AddonEnemyList.MaxEnemyCount];

            StatusSheet = _plugin.DataManager.GetExcelSheet<Status>();
        }

        public void Dispose()
        {
            if (!Built)
                return;

            DestroyNodes(_plugin.GameGui.GetAddonByName<AddonEnemyList>(AddonEnemyListHooks.AddonName));
        }

        public bool IsBuiltOn(AddonEnemyList* addon) => Built && addon != null && (nint)addon == _builtOn;

        // 節點所在的視窗已經不在（或換了實例）：不解參考、不釋放（父／兄弟指標可能指向已釋放記憶體），寧可漏一點記憶體。
        public void ForgetNodes(string reason)
        {
            var dropped = 0;
            for (var i = 0; i < NodeGroups.Length; i++)
            {
                if (NodeGroups[i] != null)
                    dropped++;
                NodeGroups[i] = null;
            }

            if (dropped > 0)
                _plugin.PluginLog.Information($"EnemyListDebuffs：{reason}，舊的 {dropped} 組弱化狀態節點不再使用（不釋放，避免碰到已釋放的視窗）。");

            _builtOn = 0;
            Built = false;
        }

        public void ForEachGroup(Action<StatusNodeGroup> func)
        {
            foreach(var group in NodeGroups)
                if (group != null)
                    func(group);
        }

        public void ForEachNode(Action<StatusNode> func)
        {
            foreach (var group in NodeGroups)
                if (group != null)
                    group.ForEachNode(func);
        }

        public void SetGroupVisibility(int index, bool enable, bool setChildren = false)
        {
            var group = NodeGroups[index];

            if (group == null)
                return;

            group.SetVisibility(enable, setChildren);
        }

        public void SetStatus(int groupIndex, int statusIndex, int id, int timer)
        {
            var group = NodeGroups[groupIndex];

            if (group == null)
                return;

            var row = StatusSheet.GetRow((uint) id);
            
            group.SetStatus(statusIndex, row.Icon, timer);
        }

        public void HideUnusedStatus(int groupIndex, int statusCount)
        {
            var group = NodeGroups[groupIndex];

            if (group == null)
                return;

            group.HideUnusedStatus(statusCount);
        }

        public void SetDepthPriority(int groupIndex, bool enable)
        {
            var group = NodeGroups[groupIndex];

            if (group == null)
                return;

            group.RootNode->SetUseDepthBasedPriority(enable);

            group.ForEachNode(node =>
            {
                node.RootNode->SetUseDepthBasedPriority(enable);
                node.DurationNode->AtkResNode.SetUseDepthBasedPriority(enable);
                node.IconNode->AtkResNode.SetUseDepthBasedPriority(enable);
            });
        }

        public void LoadConfig()
        {
            ForEachNode(node => node.LoadConfig());
            ForEachGroup(group => group.LoadConfig());
        }

        public bool BuildNodes(AddonEnemyList* enemyListAddon)
        {
            if (enemyListAddon == null) return false;
            if (IsBuiltOn(enemyListAddon)) return true;
            if (Built) ForgetNodes("重建節點時視窗實例已不同");

            // 建到一半失敗時 DestroyNodes 要認得這個實例。
            _builtOn = (nint)enemyListAddon;
            Built = true;

            for(byte i = 0; i < AddonEnemyList.MaxEnemyCount; i++)
            {
                var nodeGroup = new StatusNodeGroup(_plugin);
                var buttonComponent = *(&enemyListAddon->EnemyOneComponent)[i];
                // 台服加固：雙重間接取值後，component 指針可能為 null（該敵人格未配置／半重建）。
                // 解參考前判空；null 就 skip 該格。此時 nodeGroup 尚未配置任何原生記憶體
                // （原生節點於 nodeGroup.BuildNodes 才配置），直接略過不會洩漏。
                if (buttonComponent == null || buttonComponent->AtkComponentBase.UldManager.RootNode == null)
                    continue;

                if (!nodeGroup.BuildNodes(StartingNodeId))
                {
                    DestroyNodes(enemyListAddon);
                    return false;
                }

                var lastChild = buttonComponent->AtkComponentBase.UldManager.RootNode;
                while (lastChild->PrevSiblingNode != null) lastChild = lastChild->PrevSiblingNode;

                lastChild->PrevSiblingNode = nodeGroup.RootNode;
                nodeGroup.RootNode->NextSiblingNode = lastChild;
                nodeGroup.RootNode->ParentNode = (AtkResNode*) buttonComponent->AtkComponentBase.UldManager.RootNode;

                buttonComponent->AtkComponentBase.UldManager.UpdateDrawNodeList();

                NodeGroups[i] = nodeGroup;
            }

            return true;
        }

        public void DestroyNodes(AddonEnemyList* enemyListAddon)
        {
            if (!Built) return;
            if (!IsBuiltOn(enemyListAddon))
            {
                ForgetNodes("敵對列表視窗已不是建立節點時那一個");
                return;
            }

            for(byte i = 0; i < AddonEnemyList.MaxEnemyCount; i++)
            {
                var buttonComponent = *(&enemyListAddon->EnemyOneComponent)[i];

                // 台服加固：component 指針可能為 null（addon 半重建／釋放）。解參考前判空。
                // null 時 addon 樹已消失：不做 sibling 反鏈與 UpdateDrawNodeList（兩者都會踩已釋放記憶體＝AVE），
                // 僅丟棄我方 C# 追蹤（我方注入的原生節點已隨 addon 樹被遊戲一併釋放，不可再 Destroy＝避免二次釋放）。
                if (buttonComponent == null)
                {
                    NodeGroups[i] = null;
                    continue;
                }

                if (NodeGroups[i] != null)
                {
                    if (Unlink(buttonComponent, NodeGroups[i].RootNode))
                        NodeGroups[i].DestroyNodes();
                    else
                        _plugin.PluginLog.Information($"EnemyListDebuffs：第 {i + 1} 格的弱化狀態節點已不在原本的節點串上，不釋放、只放棄追蹤。");
                }
                NodeGroups[i] = null;

                buttonComponent->AtkComponentBase.UldManager.UpdateDrawNodeList();
            }

            _builtOn = 0;
            Built = false;
        }

        // 從元件根節點的兄弟串裡找到我方節點才拆；找不到就不碰我方節點記下的鄰居指標（可能已失效）。
        private static bool Unlink(AtkComponentButton* buttonComponent, AtkResNode* ours)
        {
            if (ours == null)
                return false;

            var node = buttonComponent->AtkComponentBase.UldManager.RootNode;
            for (var guard = 0; node != null && guard < 256; guard++)
            {
                if (node->PrevSiblingNode == ours)
                {
                    var after = ours->PrevSiblingNode;
                    node->PrevSiblingNode = after;
                    if (after != null)
                        after->NextSiblingNode = node;
                    return true;
                }

                node = node->PrevSiblingNode;
            }

            return false;
        }
    }
}
