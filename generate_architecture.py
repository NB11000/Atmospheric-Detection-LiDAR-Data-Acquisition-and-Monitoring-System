# -*- coding: utf-8 -*-
"""
生成系统架构图
高频数据采集与分发系统 V2.0 架构可视化
"""

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import matplotlib.patches as mpatches
from matplotlib.patches import FancyBboxPatch, FancyArrowPatch
import numpy as np

# 设置中文字体
plt.rcParams['font.sans-serif'] = ['SimHei', 'Microsoft YaHei', 'WenQuanYi Micro Hei', 'DejaVu Sans']
plt.rcParams['axes.unicode_minus'] = False

fig, ax = plt.subplots(1, 1, figsize=(20, 14))
ax.set_xlim(0, 20)
ax.set_ylim(0, 14)
ax.axis('off')

# ============================================================
# 颜色方案
# ============================================================
COLORS = {
    '采集层': '#E8F5E9',    # 浅绿
    '总线层': '#FFF3E0',    # 浅橙
    '持久化层': '#E3F2FD',  # 浅蓝
    '分发层': '#F3E5F5',   # 浅紫
    '检测层': '#FFEBEE',    # 浅红
    '进程': '#FAFAFA',
    'MMF': '#FFFDE7',       # 浅黄
    'MQTT': '#E0F2F1',     # 浅青
    '前端': '#FBE9E7',     # 浅橙红
    '子进程': '#E8EAF6',   # 浅靛
    '主进程': '#F1F8E9',   # 浅草绿
}

# 标题
ax.text(10, 13.5, '高频数据采集与分发系统 V2.0 — 架构总览', 
        fontsize=16, fontweight='bold', ha='center', va='center',
        bbox=dict(boxstyle='round,pad=0.3', facecolor='#37474F', edgecolor='none', alpha=0.9),
        color='white')

# ============================================================
# 定义盒子和箭头函数
# ============================================================
def draw_box(x, y, w, h, text, color='#E8F5E9', edgecolor='#2E7D32', 
             subtext=None, fontsize=10, sub_fontsize=8, linewidth=1.5):
    """绘制带标题的模块盒子"""
    box = FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0.05",
                         facecolor=color, edgecolor=edgecolor, linewidth=linewidth)
    ax.add_patch(box)
    ax.text(x + w/2, y + h/2 + 0.05, text, fontsize=fontsize, 
            fontweight='bold', ha='center', va='center', color='#333')
    if subtext:
        ax.text(x + w/2, y + h/2 - 0.25, subtext, fontsize=sub_fontsize,
                ha='center', va='center', color='#666')

def draw_arrow(start, end, color='#333', linewidth=1.5, style='arc3,rad=0',
               connectionstyle='arc3,rad=0', label='', label_size=7, 
               linestyle='solid', arrowstyle='->'):
    """绘制箭头"""
    arrow = FancyArrowPatch(start, end, 
                            arrowstyle=arrowstyle,
                            connectionstyle=connectionstyle,
                            color=color, linewidth=linewidth,
                            linestyle=linestyle)
    ax.add_patch(arrow)
    if label:
        mid = ((start[0] + end[0])/2, (start[1] + end[1])/2)
        ax.text(mid[0], mid[1] - 0.15, label, fontsize=label_size,
                ha='center', va='top', color=color,
                bbox=dict(boxstyle='round,pad=0.1', facecolor='white', edgecolor='none', alpha=0.8))

def draw_process_boundary(x, y, w, h, label, color='#E8EAF6'):
    """绘制进程边界虚线框"""
    rect = mpatches.Rectangle((x, y), w, h, fill=False, edgecolor='#1565C0',
                              linewidth=2, linestyle='--', facecolor=color, alpha=0.08)
    ax.add_patch(rect)
    ax.text(x + w/2, y + h + 0.08, f'══ {label} ══', fontsize=8,
            ha='center', va='bottom', color='#1565C0', fontweight='bold')

# ============================================================
# 图例
# ============================================================
legend_y = 0.2
legend_items = [
    ('数据采集与结构化层', '#E8F5E9', '#2E7D32'),
    ('核心数据总线层', '#FFF3E0', '#E65100'),
    ('数据持久化层', '#E3F2FD', '#1565C0'),
    ('数据分发层', '#F3E5F5', '#6A1B9A'),
    ('数据检测与工况判别层', '#FFEBEE', '#C62828'),
    ('MMF/共享内存', '#FFFDE7', '#F57F17'),
    ('MQTT发布', '#E0F2F1', '#00695C'),
]
for i, (name, fc, ec) in enumerate(legend_items):
    ax.add_patch(mpatches.Rectangle((0.3 + i*2.8, legend_y), 0.3, 0.3, 
                                     facecolor=fc, edgecolor=ec, linewidth=1.5))
    ax.text(0.7 + i*2.8, legend_y + 0.15, name, fontsize=7, va='center')

# ============================================================
# 1. 子进程 (ConsoleApp1) — 左侧
# ============================================================
sub_x, sub_y, sub_w, sub_h = 0.3, 1.8, 7.5, 10
draw_process_boundary(sub_x, sub_y, sub_w, sub_h, '子进程 ConsoleApp1 (数据采集与预处理)', '#E8EAF6')

# --- 1.1 ADWork 采集线程 ---
draw_box(0.5, 10.0, 3.2, 1.2, 'ADWork\n(数据采集线程)', COLORS['采集层'], '#2E7D32',
         subtext='硬件交互·原始二进制读取', fontsize=10)

# --- 1.2 ADDraw 预处理线程 ---
draw_box(0.5, 8.0, 3.2, 1.2, 'ADDraw\n(数据预处理线程)', COLORS['采集层'], '#2E7D32',
         subtext='去噪·通道对齐·电压转换', fontsize=10)

# --- 1.3 Analysis 分析线程 ---
draw_box(0.5, 6.0, 3.2, 1.2, 'Analysis\n(结构化分析线程)', COLORS['采集层'], '#2E7D32',
         subtext='time/CH₁/CH₂/Vis/Cn² 结构化', fontsize=10)

# --- 1.4 UI 刷新线程 ---
draw_box(4.2, 8.0, 3.2, 1.2, 'UI刷新线程', COLORS['分发层'], '#6A1B9A',
         subtext='直读ADDraw原始电压', fontsize=10)

# --- 1.5 数据检测线程 (文档中描述但代码中还未完全实现) ---
draw_box(4.2, 6.0, 3.2, 1.2, '数据检测分析线程', COLORS['检测层'], '#C62828',
         subtext='能见度·折射率·信号质量判定', fontsize=10)

# --- 1.6 持久化线程 ---
draw_box(4.2, 4.0, 3.2, 1.2, '数据持久化线程', COLORS['持久化层'], '#1565C0',
         subtext='1s/5s/30s/1min/5min 归档', fontsize=10)

# ============================================================
# 2. 共享内存层 (MMF)
# ============================================================

# --- 2.1 核心数据总线 (RawRingBuffer) ---
draw_box(0.5, 2.0, 6.9, 1.2, '核心数据总线 RawRingBuffer (环形缓冲区+MMF)', COLORS['总线层'], '#E65100',
         subtext='单写多读·所有读取端读取"写指针前一段"有效数据', fontsize=9, linewidth=2)

# --- 2.2 UI专用缓冲区 (UISharedBuffer) ---
draw_box(8.5, 8.0, 5.5, 1.2, 'UI专用缓冲区 UISharedBuffer (独立MMF)', COLORS['MMF'], '#F57F17',
         subtext='与核心数据总线完全物理隔离·BufferLength=30000 > PixelCount=1000', fontsize=9, linewidth=2)

# ============================================================
# 3. 主进程 (WebAPI) — 右侧
# ============================================================
main_x, main_y, main_w, main_h = 8.0, 1.8, 6.5, 10
draw_process_boundary(main_x, main_y, main_w, main_h, '主进程 WebAPI (托管+发布)', '#F1F8E9')

# --- 3.1 主控进程(波形发布) ---
draw_box(8.5, 6.0, 5.5, 1.2, '主控进程 WaveformPublishService', COLORS['主进程'], '#558B2F',
         subtext='33ms周期从UI专用缓冲区读取降采样数据', fontsize=9)

# --- 3.2 MQTT RPC 后台服务 ---
draw_box(8.5, 4.0, 5.5, 1.2, 'MQTT RPC 主通道服务', COLORS['主进程'], '#558B2F',
         subtext='Collector/Laser/System/Log Handler', fontsize=9)

# --- 3.3 WebSocket / SignalR ---
draw_box(8.5, 2.0, 5.5, 1.2, 'WebSocket / SignalR 实时推送', COLORS['主进程'], '#558B2F',
         subtext='/ws/ui-data · /hubs/system-state', fontsize=9)

# ============================================================
# 4. MQTT 服务器 — 右上
# ============================================================
draw_box(16.0, 9.0, 3.5, 1.8, 'MQTT 服务器\n(EMQX / Mosquitto)', COLORS['MQTT'], '#00695C',
         subtext='标准 MQTT 3.1.1/5.0', fontsize=10)

# MQTT Topics 子标签
draw_box(16.0, 7.0, 3.5, 1.5, '三层Topic隔离', '#E0F2F1', '#004D40',
         subtext='📊 低频Topic / 📈 实时Topic / 🚨 告警Topic', fontsize=9)

# ============================================================
# 5. 前端 — 最右侧
# ============================================================
draw_box(16.0, 4.5, 3.5, 1.8, '前端 Web 界面', COLORS['前端'], '#BF360C',
         subtext='趋势图表·波形渲染·告警弹窗·工况面板', fontsize=9)

# ============================================================
# 6. 数据流向箭头 (核心)
# ============================================================

# 6.1 采集硬件 → ADWork
draw_arrow((0.3, 11.2), (0.5, 11.2), color='#2E7D32', linewidth=2,
           label='1MHz 原始采样数据', label_size=7)

ax.text(-0.05, 11.2, '采集硬件', fontsize=8, fontweight='bold', ha='right', va='center',
        bbox=dict(boxstyle='round,pad=0.2', facecolor='#FFCCBC', edgecolor='#BF360C'))

# 6.2 ADWork → ADDraw (Channel内部)
draw_arrow((2.1, 10.0), (2.1, 9.2), color='#2E7D32', linewidth=2,
           connectionstyle='arc3,rad=0', label='Channel<Data_Block>', label_size=7)

# 6.3 ADDraw → Analysis (Channel内部)
draw_arrow((2.1, 8.0), (2.1, 7.2), color='#2E7D32', linewidth=2,
           connectionstyle='arc3,rad=0', label='Channel<Voltage_block>', label_size=7)

# 6.4 ADDraw → UI刷新线程 (直读原始电压 - 文档关键设计)
draw_arrow((3.7, 8.6), (4.2, 8.6), color='#6A1B9A', linewidth=2.5,
           connectionstyle='arc3,rad=0', label='直读原始电压值 (不经过核心总线)', label_size=7)

# 6.5 UI刷新线程 → UI专用缓冲区
draw_arrow((7.4, 8.6), (8.5, 8.6), color='#F57F17', linewidth=2.5,
           connectionstyle='arc3,rad=0', label='降采样1000:1 → WriteSampleBatch', label_size=7)

# 6.6 Analysis → 核心数据总线
draw_arrow((2.1, 6.0), (2.1, 3.2), color='#E65100', linewidth=2,
           connectionstyle='arc3,rad=0', label='结构化数据写入', label_size=7)

# 6.7 核心数据总线 → 检测线程
draw_arrow((3.0, 3.2), (5.8, 7.2), color='#C62828', linewidth=2,
           connectionstyle='arc3,rad=0.15', label='读取写指针前一段', label_size=7)

# 6.8 核心数据总线 → 持久化线程
draw_arrow((3.5, 2.0), (5.8, 5.2), color='#1565C0', linewidth=1.5,
           connectionstyle='arc3,rad=0.2', label='周期读取归档', label_size=7)

# 6.9 核心数据总线 → 低频UI分发 (文档中有但代码中不明确)
draw_arrow((3.0, 2.8), (8.5, 3.2), color='#6A1B9A', linewidth=1.5,
           connectionstyle='arc3,rad=0.25', label='7s周期低频数据', label_size=7)

# 6.10 UI专用缓冲区 → 主控进程
draw_arrow((11.25, 8.0), (11.25, 7.2), color='#558B2F', linewidth=2.5,
           connectionstyle='arc3,rad=0', label='ReadLatestFrame(33ms)', label_size=7)

# 6.11 主控进程 → MQTT 实时Topic
draw_arrow((14.0, 6.6), (16.0, 7.0), color='#00695C', linewidth=2,
           connectionstyle='arc3,rad=0', label='实时波形数据', label_size=7)

# 6.12 低频 → MQTT 低频Topic
draw_arrow((14.0, 3.5), (17.75, 7.0), color='#00695C', linewidth=1.5,
           connectionstyle='arc3,rad=-0.1', label='低频统计数据', label_size=7)

# 6.13 检测告警 → MQTT 告警Topic
draw_arrow((7.4, 6.0), (17.75, 7.5), color='#C62828', linewidth=2,
           connectionstyle='arc3,rad=-0.15', label='异常告警', label_size=7)

# 6.14 MQTT → 前端
draw_arrow((17.75, 9.0), (17.75, 6.3), color='#BF360C', linewidth=2,
           connectionstyle='arc3,rad=0', label='MQTT订阅', label_size=7)

# 6.15 WebSocket直连 (额外)
draw_arrow((14.0, 2.6), (16.0, 4.5), color='#BF360C', linewidth=1.5,
           connectionstyle='arc3,rad=0', label='WebSocket直连', label_size=7)

# ============================================================
# 7. 底部标注：关键设计要点
# ============================================================
notes = [
    '① IPC机制: 全链路内存映射文件(MMF)实现零拷贝, 延迟微秒级',
    '② 核心总线: 单写多读环形缓冲区, 所有读取端读"写指针前一段", 无需加锁',
    '③ 双缓冲区隔离: 核心总线(RawRingBuffer)与UI专用缓冲区(UISharedBuffer)完全物理隔离',
    '④ 三Topic隔离: 低频Topic(7s) + 实时Topic(33ms) + 告警Topic(事件触发)',
    '⑤ 子进程4线程: ADWork→ADDraw→Analysis→UI, 通过Channel无锁传递',
]
for i, note in enumerate(notes):
    ax.text(0.3, 0.7 - i*0.22, note, fontsize=7, color='#555',
            bbox=dict(boxstyle='round,pad=0.2', facecolor='#F5F5F5', edgecolor='#DDD'))


# ============================================================
# 保存输出
# ============================================================
plt.tight_layout()
output_path = 'E:\\新建文件夹 (2)\\数据采集MQTT版\\数据采集与检测系统V2.0\\架构图_系统架构总览.png'
plt.savefig(output_path, dpi=200, bbox_inches='tight', facecolor='white')
print(f'架构图已生成: {output_path}')
plt.close()
