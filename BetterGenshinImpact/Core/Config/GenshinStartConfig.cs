using CommunityToolkit.Mvvm.ComponentModel;
using System;
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
    ///     当前页面输入是否合法；仅用于阻止非法输入状态下的本次启动，不写入配置文件
    /// </summary>
    [JsonIgnore]
    public bool IsFpsUnlockTargetInputValid { get; set; } = true;

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
}
