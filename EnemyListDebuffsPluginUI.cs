using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using Dalamud.Interface.Components;

namespace EnemyListDebuffs
{
    public class EnemyListDebuffsPluginUI : IDisposable
    {
        private readonly EnemyListDebuffsPlugin _plugin;

#if DEBUG
        private bool ConfigOpen = true;
#else
        private bool ConfigOpen = false;
#endif
        public bool IsConfigOpen => ConfigOpen;

        public EnemyListDebuffsPluginUI(EnemyListDebuffsPlugin p)
        {
            _plugin = p;

            _plugin.Interface.UiBuilder.OpenConfigUi += UiBuilder_OnOpenConfigUi;
            _plugin.Interface.UiBuilder.Draw += UiBuilder_OnBuild;
        }

        public void Dispose()
        {
            _plugin.Interface.UiBuilder.OpenConfigUi -= UiBuilder_OnOpenConfigUi;
            _plugin.Interface.UiBuilder.Draw -= UiBuilder_OnBuild;
        }

        public void ToggleConfig()
        {
            ConfigOpen = !ConfigOpen;
        }

        public void UiBuilder_OnOpenConfigUi() => ConfigOpen = true;

        public void UiBuilder_OnBuild()
        {
            if (!ConfigOpen)
                return;

            ImGui.SetNextWindowSize(new Vector2(420, 647), ImGuiCond.FirstUseEver);

            if (!ImGui.Begin(_plugin.Name, ref ConfigOpen, ImGuiWindowFlags.None))
            {
                ImGui.End();
                return;
            }

            bool needSave = false;

            if (ImGui.CollapsingHeader("一般", ImGuiTreeNodeFlags.DefaultOpen))
            {
                needSave |= ImGui.Checkbox("啟用", ref _plugin.Config.Enabled);
                needSave |= ImGui.InputInt("更新間隔（毫秒）", ref _plugin.Config.UpdateInterval, 10);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("狀態更新的間隔時間（毫秒）");
                if (ImGui.Button("重設為預設值"))
                {
                    _plugin.Config.SetDefaults();
                    needSave = true;
                }
                ImGui.Text("設定視窗開啟時，會顯示測試節點以協助設定。");
            }

            if (ImGui.CollapsingHeader("節點群組", ImGuiTreeNodeFlags.DefaultOpen))
            {
                needSave |= ImGui.Checkbox("由右側開始填滿", ref _plugin.Config.FillFromRight);
                needSave |= ImGui.SliderInt("X 偏移", ref _plugin.Config.GroupX, -200, 200);
                needSave |= ImGui.SliderInt("Y 偏移", ref _plugin.Config.GroupY, -200, 200);
                needSave |= ImGui.SliderInt("節點間距", ref _plugin.Config.NodeSpacing, -5, 30);
                needSave |= ImGui.SliderFloat("群組縮放", ref _plugin.Config.Scale, 0.01F, 3.0F);
            }

            if (ImGui.CollapsingHeader("節點", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Text("建議維持圖示寬度：高度為 3:4 的比例以獲得最佳效果。");
                needSave |= ImGui.SliderInt("圖示 X 偏移", ref _plugin.Config.IconX, -200, 200);
                needSave |= ImGui.SliderInt("圖示 Y 偏移", ref _plugin.Config.IconY, -200, 200);
                needSave |= ImGui.SliderInt("圖示寬度", ref _plugin.Config.IconWidth, 5, 100);
                needSave |= ImGui.SliderInt("圖示高度", ref _plugin.Config.IconHeight, 5, 100);
                needSave |= ImGui.SliderInt("持續時間 X 偏移", ref _plugin.Config.DurationX, -200, 200);
                needSave |= ImGui.SliderInt("持續時間 Y 偏移", ref _plugin.Config.DurationY, -200, 200);
                needSave |= ImGui.SliderInt("持續時間字型大小", ref _plugin.Config.FontSize, 1, 60);
                needSave |= ImGui.SliderInt("持續時間間距", ref _plugin.Config.DurationPadding, -100, 100);

                needSave |= ImGui.ColorEdit4("持續時間文字顏色", ref _plugin.Config.DurationTextColor);
                needSave |= ImGui.ColorEdit4("持續時間邊框顏色", ref _plugin.Config.DurationEdgeColor);
            }

            if (needSave)
            {
                _plugin.StatusNodeManager.LoadConfig();
                _plugin.Config.Save();
            }

            ImGui.End();
        }
    }
}
