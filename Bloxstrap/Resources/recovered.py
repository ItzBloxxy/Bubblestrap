import os
import xml.etree.ElementTree as ET
import xml.dom.minidom as minidom
import requests
import time
import shutil
from deep_translator import GoogleTranslator

SERVER_URL = "http://localhost:5000/translate"

DEEPL_SUPPORTED = [
    'ar', 'bg', 'cs', 'da', 'de', 'el', 'en', 'es', 'et', 'fi', 'fr', 'hu', 'id', 
    'it', 'ja', 'ko', 'lt', 'lv', 'nb', 'nl', 'pl', 'pt', 'ro', 'ru', 'sk', 'sl', 
    'sv', 'tr', 'uk', 'zh'
]

GOOGLE_MAP = {
    'fil': 'tl',
    'zh-cn': 'zh-CN',
    'zh-tw': 'zh-TW'
}

def save_resx_pretty(tree, filename):
    raw_xml = ET.tostring(tree.getroot(), encoding='utf-8')
    reparsed = minidom.parseString(raw_xml)
    pretty_xml = reparsed.toprettyxml(indent="  ")
    cleaned_xml = "\n".join([line for line in pretty_xml.splitlines() if line.strip()])
    with open(filename, "w", encoding="utf-8") as f:
        f.write(cleaned_xml)

def main():
    if not os.path.exists("Strings.resx"):
        print(f"Error: Strings.resx not found.")
        return

    primary_tree = ET.parse("Strings.resx")
    source_root = primary_tree.getroot()
    source_data = {d.get('name'): d.find('value').text for d in source_root.findall('data') if d.find('value') is not None}
    valid_keys = set(source_data.keys())

    target_files = [f for f in os.listdir('.') if f.startswith('Strings.') and f.endswith('.resx') and f != "Strings.resx"]

    for filename in target_files:
        parts = filename.split('.')
        raw_code = parts[1].lower() 
        
        if raw_code.startswith("en"):
            shutil.copy("Strings.resx", filename)
            print(f"✅ {filename} synced directly from base.")
            continue

        base_code = raw_code.split('-')[0]
        use_google = base_code not in DEEPL_SUPPORTED
        engine_name = "Google (Fallback)" if use_google else "DeepL (Primary)"
        
        if not use_google:
            target_send = raw_code if base_code in ['zh', 'pt'] else base_code
        else:
            target_send = GOOGLE_MAP.get(raw_code, GOOGLE_MAP.get(base_code, base_code))

        print(f"\n--- 🌍 Processing {filename} ({target_send}) via {engine_name} ---")
        
        tree = ET.parse(filename)
        root = tree.getroot()

        removed_count = 0
        for data_tag in root.findall('data'):
            if data_tag.get('name') not in valid_keys:
                root.remove(data_tag)
                removed_count += 1
        
        if removed_count > 0:
            print(f"Sweep: Removed {removed_count} unused keys from {filename}")
            save_resx_pretty(tree, filename)

        to_translate = []
        for key in source_data:
            existing_node = root.find(f"./data[@name='{key}']/value")
            if existing_node is None or not existing_node.text or not existing_node.text.strip():
                to_translate.append(key)

        if not to_translate:
            print(f"✅ {filename} is already fully translated.")
            save_resx_pretty(tree, filename) 
            continue

        for key in to_translate:
            english_text = source_data[key]
            if not english_text: continue

            try:
                if use_google:
                    translated_text = GoogleTranslator(source='auto', target=target_send).translate(english_text)
                    time.sleep(0.1) 
                else:
                    response = requests.post(SERVER_URL, json={"text": english_text, "lang": target_send}, timeout=60)
                    translated_text = response.json().get('translated_text') if response.status_code == 200 else None

                if translated_text:
                    print(f"  [+] {key} -> {translated_text}")
                    data_elem = root.find(f"./data[@name='{key}']")
                    if data_elem is None:
                        data_elem = ET.SubElement(root, 'data', name=key)
                        data_elem.set('xml:space', 'preserve')
                    
                    val_elem = data_elem.find('value')
                    if val_elem is None:
                        val_elem = ET.SubElement(data_elem, 'value')
                    
                    val_elem.text = translated_text
                    save_resx_pretty(tree, filename)
                
                if not use_google:
                    time.sleep(0.5)

            except Exception as e:
                print(f"  [!] Failed '{key}': {e}")
                time.sleep(2)

if __name__ == "__main__":
    main()