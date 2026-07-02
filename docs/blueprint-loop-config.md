# 蓝图刷点关键配置

当前默认推荐蓝图码：`123 675 780`。

默认刷图参数：

- 每轮技术点：`50`
- 蓝图净时间：`101` 秒
- 原循环额外时间：`25.3` 秒
- 完整循环估算：`126.3` 秒
- `Enter -> X` 默认等待：`115` 秒
- 刷分车性能分：`834`

蓝图封得很快，不应只依赖固定蓝图码。推荐到这个视频下面找当前可用蓝图，尤其是评论区玩家复制的蓝图码：

https://www.bilibili.com/video/BV1TwjN6NELh/?vd_source=6832a0a0a4121c57db1c741922b502bd

如果换用其它蓝图，玩家需要先手动跑一次，记录每次跑图获得的技术点、结算显示的蓝图净时间，以及分享者要求的游戏设置。运行模式 `3` 重设设置时，程序会前置询问每轮技术点、蓝图净时间、刷分车性能分和点技能/删车性能分。

当前默认推荐蓝图需要：

- 自动转向
- 手动挡
- 牵引力控制系统开启
- 稳定控制系统开启
- 游戏设置“抬头显示 > 技术动画”关闭

如果自己使用其它蓝图，需要根据分享者提供的设置调整。

配置字段：

```json
{
  "blueprint_skill_points_per_run": 50,
  "blueprint_net_time_ms": 101000,
  "blueprint_loop_extra_ms": 25300,
  "blueprint_after_x_wait_ms": 1000,
  "blueprint_post_enter_wait_ms": 10000,
  "drive_vehicle_performance_score": 834,
  "skill_vehicle_performance_score": 600
}
```

完整循环估算使用：

```text
blueprint_net_time_ms + blueprint_loop_extra_ms
```

`Enter -> X` 等待由完整循环估算扣掉固定输入/确认等待得到。默认值为 `115` 秒；菜单 `4` 调整 Enter->X 等待时，会改写 `blueprint_loop_extra_ms`。
