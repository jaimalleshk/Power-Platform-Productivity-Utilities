import json
import os

transcript_path = r"C:\Users\jaima\.gemini\antigravity\brain\9f1ff5f5-ea84-4c0f-a8e1-e5ca8096eb37\.system_generated\logs\transcript_full.jsonl"
out_path = r"D:\OneDrive\OneDrive-Projects\Claude D365 Consulting\PowerPlatformUtilities\new_images_analysis.txt"

if not os.path.exists(transcript_path):
    print("Transcript not found")
    exit(1)

with open(transcript_path, "r", encoding="utf-8") as f, open(out_path, "w", encoding="utf-8") as out:
    for line_num, line in enumerate(f, 1):
        if any(img in line for img in ["IMG_4006", "IMG_4007", "IMG_4009", "IMG_4010", "IMG_4011", "IMG_4012", "IMG_4013"]):
            try:
                data = json.loads(line)
                out.write(f"=== Line {line_num} (Type: {data.get('type')}, Source: {data.get('source')}, Status: {data.get('status')}) ===\n")
                content = data.get("content", "")
                if content:
                    out.write(content)
                    out.write("\n" + "-"*40 + "\n")
                if "tool_calls" in data:
                    out.write(f"Tool Calls: {json.dumps(data['tool_calls'], indent=2)}\n")
                out.write("\n" + "="*80 + "\n\n")
            except Exception as e:
                out.write(f"Line {line_num} error parsing: {e}\n")

print(f"Done. Output written to {out_path}")
