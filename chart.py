import datetime
import os
import matplotlib
import matplotlib.dates as mdates
import matplotlib.font_manager as fm
import matplotlib.pyplot as plt
import numpy as np
import pandas as pd
import requests

from scipy.interpolate import PchipInterpolator

OWNER = "itzbloxxy"
REPO = "Vantastrap"
RELEASES_URL = f"https://api.github.com/repos/{OWNER}/{REPO}/releases"
FONT_PATH = "fonts/Caveat.ttf"
OUTPUT_PATH = "downloads.png"


def setup_font():
    fm.fontManager.addfont(FONT_PATH)
    font = fm.FontProperties(fname=FONT_PATH, size=16)
    matplotlib.rcParams["font.family"] = font.get_name()
    return font


def build_headers():
    headers = {
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2026-03-10",
    }
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    return headers


def fetch_all_releases(headers):
    releases = []
    url = RELEASES_URL
    params = {"per_page": 100}

    while url:
        response = requests.get(url, headers=headers, params=params)

        if response.status_code == 403 and response.headers.get("X-RateLimit-Remaining") == "0":
            reset_time = int(response.headers.get("X-RateLimit-Reset", 0))
            reset_dt = datetime.datetime.fromtimestamp(reset_time)
            raise RuntimeError(f"Rate limited. Resets at {reset_dt}. Set GITHUB_TOKEN to raise the limit.")

        response.raise_for_status()
        releases.extend(response.json())

        url = response.links.get("next", {}).get("url")
        params = None

    return releases


def releases_to_dataframe(releases):
    rows = []
    for release in releases:
        created_at = release.get("published_at") or release.get("created_at")
        if not created_at:
            continue
        date = datetime.datetime.strptime(created_at, "%Y-%m-%dT%H:%M:%SZ")
        downloads = sum(asset.get("download_count", 0) for asset in release.get("assets", []))
        rows.append({"Date": date, "Downloads": downloads})

    df = pd.DataFrame(rows)
    if df.empty:
        return df

    df = df.groupby("Date", as_index=False).sum()
    df = df.sort_values(by="Date").reset_index(drop=True)
    df["Total"] = df["Downloads"].cumsum()
    return df


def plot_smoothed_line(ax, df):
    if len(df) > 3:
        x_num = mdates.date2num(df["Date"])
        x_smooth = np.linspace(x_num.min(), x_num.max(), 300)
        y_smooth = PchipInterpolator(x_num, df["Total"])(x_smooth)
        x_values = mdates.num2date(x_smooth)
        y_values = y_smooth
    else:
        x_values = df["Date"]
        y_values = df["Total"]

    line, = ax.plot(x_values, y_values, color="#ff6b6b", linewidth=2.5, zorder=3)
    line.set_sketch_params(scale=1, length=80, randomness=20)


def style_axes(ax, df, font):
    text_color = "#ffffff"

    y_max = df["Total"].max()
    y_pad = y_max * 0.08 if y_max > 0 else 1
    ax.set_ylim(0, y_max + y_pad)

    x_min, x_max = df["Date"].min(), df["Date"].max()
    x_pad = (x_max - x_min) * 0.05 if x_max != x_min else datetime.timedelta(days=2)
    ax.set_xlim(x_min - x_pad, x_max + x_pad)

    ax.xaxis.set_major_locator(mdates.MonthLocator())
    ax.xaxis.set_major_formatter(plt.FuncFormatter(lambda x, pos=None: mdates.num2date(x).strftime("%B").lower()))
    ax.tick_params(colors=text_color, labelsize=13, length=0, rotation=0)

    for label in ax.get_xticklabels() + ax.get_yticklabels():
        label.set_fontproperties(font)

    ax.spines["top"].set_visible(False)
    ax.spines["right"].set_visible(False)
    ax.spines["left"].set_color(text_color)
    ax.spines["left"].set_linewidth(1.2)
    ax.spines["bottom"].set_color(text_color)
    ax.spines["bottom"].set_linewidth(1.2)

    ax.set_title("downloads", color=text_color, fontsize=20, fontproperties=font, pad=12)
    ax.grid(False)


def render_chart(df, font):
    fig, ax = plt.subplots(figsize=(10, 5))
    fig.patch.set_facecolor("none")
    ax.set_facecolor("none")

    plot_smoothed_line(ax, df)
    style_axes(ax, df, font)

    plt.tight_layout()
    plt.savefig(OUTPUT_PATH, dpi=300, transparent=True)


def main():
    font = setup_font()
    headers = build_headers()

    try:
        releases = fetch_all_releases(headers)
    except (requests.RequestException, RuntimeError) as error:
        print(f"Failed to fetch releases: {error}")
        return

    df = releases_to_dataframe(releases)
    if df.empty:
        print("No release data found")
        return

    render_chart(df, font)
    print(f"Saved {OUTPUT_PATH}")


if __name__ == "__main__":
    main()
