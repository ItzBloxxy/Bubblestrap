import datetime
import matplotlib.pyplot as plt
import matplotlib.dates as mdates
import matplotlib.font_manager as fm
import numpy as np
import pandas as pd
import requests
from scipy.interpolate import make_interp_spline

font_path = "fonts/Caveat.ttf"
fm.fontManager.addfont(font_path)
comic_prop = fm.FontProperties(fname=font_path, size=16)

import matplotlib
matplotlib.rcParams['font.family'] = comic_prop.get_name()

URL = "https://api.github.com/repos/itzbloxxy/bubblestrap/releases"
response = requests.get(URL)

if response.status_code == 200:
    releases = response.json()
    data = []
    for r in releases:
        created_at = r.get("published_at") or r.get("created_at")
        d = datetime.datetime.strptime(created_at, "%Y-%m-%dT%H:%M:%SZ")
        dl = sum(a.get("download_count", 0) for a in r.get("assets", []))
        data.append({"Date": d, "Downloads": dl})
    df = pd.DataFrame(data)

    if not df.empty:
        df = df.groupby("Date", as_index=False).sum()
        df = df.sort_values(by="Date").reset_index(drop=True)
        df["Total"] = df["Downloads"].cumsum()

        fig, ax = plt.subplots(figsize=(10, 5))
        fig.patch.set_facecolor('none')
        ax.set_facecolor('none')

        if len(df) > 3:
            x_num = mdates.date2num(df["Date"])
            x_smooth = np.linspace(x_num.min(), x_num.max(), 300)
            spl = make_interp_spline(x_num, df["Total"], k=3)
            y_smooth = spl(x_smooth)
            line, = ax.plot(
                mdates.num2date(x_smooth), y_smooth,
                color="#ff6b6b", linewidth=2.5, zorder=3
            )
            line.set_sketch_params(scale=1, length=80, randomness=20)
        else:
            line, = ax.plot(
                df["Date"], df["Total"],
                color="#ff6b6b", linewidth=2.5, zorder=3
            )
            line.set_sketch_params(scale=1, length=80, randomness=20)

        text_color = "#ffffff"
        y_min, y_max = df["Total"].min(), df["Total"].max()
        y_range = y_max - y_min if y_max != y_min else max(y_max * 0.1, 1)
        pad = y_range * 0.15
        ax.set_ylim(max(0, y_min - pad), y_max + pad)

        x_min, x_max = df["Date"].min(), df["Date"].max()
        x_pad = (x_max - x_min) * 0.05 if x_max != x_min else datetime.timedelta(days=2)
        ax.set_xlim(x_min - x_pad, x_max + x_pad)
        def format_date(x, pos=None):
            return mdates.num2date(x).strftime('%B').lower()

        ax.xaxis.set_major_locator(mdates.MonthLocator())
        ax.xaxis.set_major_formatter(plt.FuncFormatter(format_date))
        ax.tick_params(colors=text_color, labelsize=13, length=0, rotation=0)

        for label in ax.get_xticklabels() + ax.get_yticklabels():
            label.set_fontproperties(comic_prop)

        ax.spines["top"].set_visible(False)
        ax.spines["right"].set_visible(False)
        ax.spines["left"].set_color(text_color)
        ax.spines["left"].set_linewidth(1.2)
        ax.spines["bottom"].set_color(text_color)
        ax.spines["bottom"].set_linewidth(1.2)

        ax.set_title('downloads', color=text_color, fontsize=20,
                     fontproperties=comic_prop, pad=12)

        ax.grid(False)
        plt.tight_layout()
        plt.savefig("downloads.png", dpi=300, transparent=True)
        print("Saved downloads.png")
    else:
        print("No release data found")
else:
    print(f"Failed to fetch releases: {response.status_code}")
