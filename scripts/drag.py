# -*- coding: utf-8 -*-
"""用 SendInput 绝对坐标模拟拖拽窗口内 resizer（参数：起点屏幕坐标 x y，dx dy）。"""
import ctypes
import ctypes.wintypes as wt
import sys
import time

user32 = ctypes.windll.user32

INPUT_MOUSE = 0
MOUSEEVENTF_MOVE = 0x0001
MOUSEEVENTF_LEFTDOWN = 0x0002
MOUSEEVENTF_LEFTUP = 0x0004
MOUSEEVENTF_ABSOLUTE = 0x8000


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [("dx", wt.LONG), ("dy", wt.LONG), ("mouseData", wt.DWORD),
                ("flags", wt.DWORD), ("time", wt.DWORD), ("extra", ctypes.POINTER(wt.ULONG))]


class _INPUTunion(ctypes.Union):
    _fields_ = [("mi", MOUSEINPUT)]


class INPUT(ctypes.Structure):
    _fields_ = [("type", wt.DWORD), ("union", _INPUTunion)]


def send(inp):
    user32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(INPUT))


def move_abs(x, y):
    sw, sh = user32.GetSystemMetrics(0), user32.GetSystemMetrics(1)
    ax, ay = int(x * 65535 / (sw - 1)), int(y * 65535 / (sh - 1))
    i = INPUT(type=INPUT_MOUSE)
    i.union.mi.dx, i.union.mi.dy = ax, ay
    i.union.mi.flags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE
    send(i)


def button(flag):
    i = INPUT(type=INPUT_MOUSE)
    i.union.mi.flags = flag
    send(i)


def drag(sx, sy, dx, dy=0, steps=12):
    move_abs(sx, sy)
    time.sleep(0.5)
    button(MOUSEEVENTF_LEFTDOWN)
    time.sleep(0.2)
    for i in range(1, steps + 1):
        move_abs(sx + dx * i // steps, sy + dy * i // steps)
        time.sleep(0.045)
    time.sleep(0.15)
    button(MOUSEEVENTF_LEFTUP)


if __name__ == '__main__':
    sx, sy, dx = int(sys.argv[1]), int(sys.argv[2]), int(sys.argv[3])
    drag(sx, sy, dx)
    print(f"dragged ({sx},{sy}) dx={dx}")
