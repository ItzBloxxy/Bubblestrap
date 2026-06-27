import datetime
import os
import urllib.request
import matplotlib.pyplot as plt
import matplotlib.dates as mdates
import matplotlib.font_manager as fm
import pandas as pd
import requests

font_url = "https://github.com/google/fonts/raw/main/ofl/comicneue/ComicNeue-Regular.ttf"
font_path = "ComicNeue-Regular.ttf"
try:
    urllib.request.urlretrieve(font_url, font_path)
    fm.fontManager.addfont(font_path)
except Exception:
    pass

plt.xkcd()

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
        df = df.sort_values(by="Date").reset_index(drop=True)
        df["Total"] = df["Downloads"].cumsum()
        
        fig, ax = plt.subplots(figsize=(10, 5))
        
        fig.patch.set_facecolor('none')
        ax.set_facecolor('none')
        
        ax.plot(
            df["Date"], df["Total"], color="#ff6b6b", linewidth=3, marker="o"
        )
        
        text_color = "#ffffff"
        
        ax.set_title("Download History", fontsize=16, fontweight="bold", pad=15, color=text_color)
        ax.xaxis.set_major_formatter(mdates.DateFormatter('%Y, %B'))
        
        ax.tick_params(colors=text_color, labelsize=11)
        ax.spines["bottom"].set_color(text_color)
        ax.spines["left"].set_color(text_color)
        ax.spines["top"].set_visible(False)
        ax.spines["right"].set_visible(False)
        ax.grid(True, alpha=0.15, color=text_color)
        
        plt.xticks(rotation=25)
        plt.tight_layout()

        plt.savefig("downloads.png", dpi=300, transparent=True)

if os.path.exists(font_path):
    os.remove(font_path)
