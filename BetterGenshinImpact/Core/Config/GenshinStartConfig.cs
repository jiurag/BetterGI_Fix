using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Globalization;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     原神启动配置
/// </summary>
[Serializable]
public partial class GenshinStartConfig : ObservableObject
{
    /// <summary>
    ///     帧率解锁允许的最小目标帧率
    /// </summary>
    public const int MinFpsUnlockTarget = 30;

    /// <summary>
    ///     帧率解锁允许的最大目标帧率
    /// </summary>
    public const int MaxFpsUnlockTarget = 240;

    /// <summary>
    ///     帧率解锁默认目标帧率
    /// </summary>
    public const int DefaultFpsUnlockTarget = 120;

    /// <summary>
    ///     FOV 调整允许的最小值
    /// </summary>
    public const int MinFovTarget = 15;

    /// <summary>
    ///     FOV 调整允许的最大值
    /// </summary>
    public const int MaxFovTarget = 165;

    /// <summary>
    ///     FOV 调整默认值
    /// </summary>
    public const int DefaultFovTarget = 60;

    // /// <summary>
    // ///     自动点击月卡
    // /// </summary>
    // [ObservableProperty]
    // private bool _autoClickBlessingOfTheWelkinMoonEnabled;

    /// <summary>
    ///     自动进入游戏（开门）
    /// </summary>
    [ObservableProperty]
    private bool _autoEnterGameEnabled = true;

    /// <summary>
    ///     是否在 BetterGI 启动原神时强制使用全屏无边框
    /// </summary>
    [ObservableProperty]
    private bool _borderlessFullscreenEnabled;

    /// <summary>
    ///     是否在 BetterGI 启动原神时解锁帧率
    /// </summary>
    [ObservableProperty]
    private bool _fpsUnlockEnabled;

    /// <summary>
    ///     已保存的合法目标帧率；页面上的临时非法输入不会写入此属性
    /// </summary>
    [ObservableProperty]
    private int _fpsUnlockTarget = DefaultFpsUnlockTarget;

    /// <summary>
    ///     是否在 BetterGI 启动国服原神时去除场景雾
    /// </summary>
    [ObservableProperty]
    private bool _fogRemovalEnabled;

    /// <summary>
    ///     是否在 BetterGI 启动国服原神时调整 FOV
    /// </summary>
    [ObservableProperty]
    private bool _fovAdjustmentEnabled;

    /// <summary>
    ///     是否强制所有经过 FOV Hook 的镜头写入使用目标 FOV
    /// </summary>
    [ObservableProperty]
    private bool _fovForceModeEnabled;

    /// <summary>
    ///     已保存的合法 FOV；页面上的临时非法输入不会写入此属性
    /// </summary>
    [ObservableProperty]
    private int _fovTarget = DefaultFovTarget;

    /// <summary>
    ///     是否隐藏地图任务横幅
    /// </summary>
    [ObservableProperty]
    private bool _hideQuestBannerEnabled;

    /// <summary>
    ///     是否移除剧情和技能等事件镜头移动
    /// </summary>
    [ObservableProperty]
    private bool _removeCameraMoveEnabled;

    /// <summary>
    ///     是否移除队伍配置进度条
    /// </summary>
    [ObservableProperty]
    private bool _removeTeamProgressEnabled;

    /// <summary>
    ///     是否加速调整时间后的界面动画
    /// </summary>
    [ObservableProperty]
    private bool _speedUpClockAnimationEnabled;

    /// <summary>
    ///     是否移除水下马赛克
    /// </summary>
    [ObservableProperty]
    private bool _removeDiveMosaicEnabled;

    /// <summary>
    ///     是否把合成入口直接重定向到合成页面
    /// </summary>
    [ObservableProperty]
    private bool _redirectSynthesisEnabled;

    /// <summary>
    ///     是否移除角色被镜头贴近时的虚化
    /// </summary>
    [ObservableProperty]
    private bool _removeCharacterFadeEnabled;

    /// <summary>
    ///     当前页面输入是否合法；仅用于阻止非法输入状态下的本次启动，不写入配置文件
    /// </summary>
    [JsonIgnore]
    public bool IsFpsUnlockTargetInputValid { get; set; } = true;

    /// <summary>
    ///     当前 FOV 页面输入是否合法；仅用于阻止非法输入状态下的功能启用，不写入配置文件
    /// </summary>
    [JsonIgnore]
    public bool IsFovTargetInputValid { get; set; } = true;

    /// <summary>
    ///     原神启动参数
    /// </summary>
    [ObservableProperty]
    private string _genshinStartArgs = "";

    /// <summary>
    ///     原神安装路径
    /// </summary>
    [ObservableProperty]
    private string _installPath = "";

    /// <summary>
    ///     联动启动原神本体
    /// </summary>
    [ObservableProperty]
    private bool _linkedStartEnabled = true;

    /// <summary>
    ///     使用Starward同步记录时间
    /// </summary>
    [ObservableProperty]
    private bool _recordGameTimeEnabled = false;

    [ObservableProperty]
    private bool _startGameWithCmd = false;

    /// <summary>
    ///     启动前自动关闭原神 HDR（删除原神 HDR 对应注册表键）
    /// </summary>
    [ObservableProperty]
    private bool _autoDisableGenshinHdrEnabled = true;

    /// <summary>
    ///     判断帧率解锁目标是否在允许范围内
    /// </summary>
    /// <param name="target">待校验的目标帧率</param>
    /// <returns>目标帧率合法时返回 true</returns>
    public static bool IsValidFpsUnlockTarget(int target)
    {
        return target is >= MinFpsUnlockTarget and <= MaxFpsUnlockTarget;
    }

    /// <summary>
    ///     判断 FOV 目标是否在允许范围内
    /// </summary>
    /// <param name="target">待校验的 FOV</param>
    /// <returns>目标值合法时返回 true</returns>
    public static bool IsValidFovTarget(int target)
    {
        return target is >= MinFovTarget and <= MaxFovTarget;
    }

    /// <summary>
    ///     使用不受当前系统区域设置影响的格式解析 FOV 输入
    /// </summary>
    /// <param name="text">输入框中的原始文本</param>
    /// <param name="target">解析成功且范围合法时的目标值</param>
    /// <returns>文本是合法整数且在支持范围内时返回 true</returns>
    public static bool TryParseFovTarget(string? text, out int target)
    {
        return int.TryParse(
                   text,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out target)
               && IsValidFovTarget(target);
    }

}
