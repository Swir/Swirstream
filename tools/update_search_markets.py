"""Rebuild the shared search vocabulary from an iptv-org countries.json snapshot."""
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("countries", type=Path)
args = parser.parse_args()
repo = Path(__file__).resolve().parents[1]
terms = """
eng|live TV|live radio
pol|telewizja na żywo|radio na żywo
deu|Livestream Fernsehen|Radio Livestream
fra|télévision en direct|radio en direct
spa|televisión en directo|radio en vivo
por|TV ao vivo|rádio ao vivo
ita|TV diretta|radio in diretta
ara|تلفزيون بث مباشر|راديو بث مباشر
jpn|テレビ 生放送|ラジオ 生放送
kor|실시간 방송|실시간 라디오
zho|电视直播|电台直播
yue|電視直播|電台直播
hin|लाइव टीवी|लाइव रेडियो
tam|நேரலை தொலைக்காட்சி|நேரலை வானொலி
ben|সরাসরি টিভি|সরাসরি রেডিও
urd|براہ راست ٹی وی|براہ راست ریڈیو
fas|پخش زنده تلویزیون|پخش زنده رادیو
prd|پخش زنده تلویزیون|پخش زنده رادیو
tur|canlı TV|canlı radyo
nld|televisie live kijken|radio live luisteren
rus|телевидение прямой эфир|радио прямой эфир
ukr|телебачення наживо|радіо наживо
ces|televize živě|rádio živě
slk|televízia naživo|rádio naživo
hrv|televizija uživo|radio uživo
bos|televizija uživo|radio uživo
srp|телевизија уживо|радио уживо
cnr|televizija uživo|radio uživo
slv|televizija v živo|radio v živo
bul|телевизия на живо|радио на живо
bel|тэлебачанне ў прамым эфіры|радыё ў прамым эфіры
mkd|телевизија во живо|радио во живо
ron|televiziune în direct|radio în direct
hun|élő TV adás|élő rádió
sqi|televizion drejtpërdrejt|radio drejtpërdrejt
swe|direktsänd tv|direktsänd radio
nor|direkte TV|direkte radio
dan|direkte tv|direkte radio
fin|TV suora lähetys|radio suorana
isl|sjónvarp í beinni|útvarp í beinni
ell|τηλεόραση σε ζωντανή μετάδοση|ραδιόφωνο ζωντανά
est|televisiooni otseülekanne|raadio otse
lav|televīzija tiešraidē|radio tiešraidē
lit|televizija tiesiogiai|radijas tiesiogiai
kat|ტელევიზია პირდაპირ ეთერში|რადიო პირდაპირ ეთერში
hye|հեռուստատեսություն ուղիղ եթեր|ռադիո ուղիղ եթեր
heb|טלוויזיה שידור חי|רדיו שידור חי
swa|televisheni mubashara|redio mubashara
afr|regstreekse TV|regstreekse radio
msa|TV siaran langsung|radio siaran langsung
ind|TV siaran langsung|radio siaran langsung
vie|truyền hình trực tiếp|radio trực tiếp
tha|โทรทัศน์ถ่ายทอดสด|วิทยุออนไลน์
tgl|live na telebisyon|live na radyo
fil|live na telebisyon|live na radyo
aze|canlı televiziya|canlı radio
kaz|теледидар тікелей эфир|радио тікелей эфир
mon|телевиз шууд эфир|радио шууд эфир
cat|televisió en directe|ràdio en directe
nep|प्रत्यक्ष टेलिभिजन|प्रत्यक्ष रेडियो
sin|සජීවී රූපවාහිනී|සජීවී ගුවන් විදුලිය
mya|ရုပ်မြင်သံကြား တိုက်ရိုက်|ရေဒီယို တိုက်ရိုက်
khm|ទូរទស្សន៍ផ្សាយផ្ទាល់|វិទ្យុផ្សាយផ្ទាល់
amh|ቀጥታ የቴሌቪዥን ስርጭት|ቀጥታ የሬዲዮ ስርጭት
kir|телеканал түз эфир|радио түз эфир
"""
words = {lang: (tv, radio) for lang, tv, radio in (line.split("|") for line in terms.strip().splitlines())}
rows = []
seen = set()
for market in json.loads(args.countries.read_bytes()):
    code = market["code"].replace("UK", "GB")
    if not (len(code) == 2 and code.isascii() and code.isupper() and code.isalpha()) or code in seen:
        raise ValueError(f"Invalid or duplicate market code {code!r}")
    if not market["name"] or any(ch in market["name"] for ch in "\r\n\t"):
        raise ValueError(f"Invalid market name for {code}")
    seen.add(code)
    variants = list(dict.fromkeys(words[lang] for lang in market["languages"] if lang in words))
    # Keep an English variant for multilingual markets while using local vocabulary first.
    if len(variants) > 1 and variants[0] == words["eng"]:
        variants.append(variants.pop(0))
    if code in ("TW", "HK", "MO"):
        variants = [words["yue"]] + [v for v in variants if v not in (words["yue"], words["zho"])]
    if not variants:
        variants = [words["eng"]]
    rows.append("\t".join([code, market["name"]] + [part for pair in variants for part in pair]))
if len(seen) < 200:
    raise ValueError("Incomplete country catalogue")
target = repo / "shared/discovery/world-search.tsv"
target.parent.mkdir(parents=True, exist_ok=True)
target.write_text("\n".join(sorted(rows)) + "\n", encoding="utf-8", newline="\n")
