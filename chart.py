import datetime
import matplotlib.pyplot as plt
import matplotlib.dates as mdates
import numpy as np
import pandas as pd
import requests
from scipy.interpolate import make_interp_spline

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
            x_smooth = np.linspace(x_num.min(), x_num.max(), 50)
            spl = make_interp_spline(x_num, df["Total"], k=3)
            y_smooth = spl(x_smooth)

            with plt.rc_context({"path.sketch": (2, 15, 1)}):
                ax.plot(
                    mdates.num2date(x_smooth), y_smooth, color="#ff6b6b", linewidth=3
                )
            ax.plot(
                df["Date"], df["Total"], color="#ff6b6b", marker="o", linestyle="None"
            )
        else:
            with plt.rc_context({"path.sketch": (2, 15, 1)}):
                ax.plot(
                    df["Date"], df["Total"], color="#ff6b6b", linewidth=3, marker="o"
                )
        
        text_color = "#ffffff"
        ax.xaxis.set_major_formatter(mdates.DateFormatter('%Y, %B'))
        ax.tick_params(colors=text_color, labelsize=11)
        
        ax.spines["top"].set_visible(False)
        ax.spines["right"].set_visible(False)
        ax.spines["bottom"].set_color(text_color)
        ax.spines["left"].set_color(text_color)
        ax.grid(False)
        
        plt.xticks(rotation=25)
        plt.tight_layout()

        plt.savefig("downloads.png", dpi=300, transparent=True)
