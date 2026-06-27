import datetime
import matplotlib.pyplot as plt
import pandas as pd
import requests

# Fetch data for Bubblestrap
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
        plt.style.use("dark_background")
        fig, ax = plt.subplots(figsize=(10, 5))
        ax.plot(
            df["Date"], df["Total"], color="#ff6b6b", linewidth=3, marker="o"
        )
        ax.set_title(
            "Download History", fontsize=14, fontweight="bold", pad=15
        )
        ax.grid(True, linestyle="--", alpha=0.15)
        ax.spines["top"].set_visible(False)
        ax.spines["right"].set_visible(False)
        plt.xticks(rotation=25)
        plt.tight_layout()

        plt.savefig("downloads.png", dpi=300)