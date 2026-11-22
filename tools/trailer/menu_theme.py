"""Extracts Big Walk's menu music ("Menu Music LOOP v3.0", 4 min 12 s) from the installed game as a WAV, for the trailer's
soundtrack (encode.ps1 -Music). Needs UnityPy (pip install --user UnityPy), which brings FMOD's decoder.

  py.cmd tools/trailer/menu_theme.py [--game "G:/SteamLibrary/steamapps/common/Big Walk"] [--out menu-theme.wav]

The clip lives in data.unity3d as an AudioClip whose samples are streamed from a .resource file beside it (an FSB5 bank,
Vorbis). UnityPy looks for that file inside the bundle and hands FMOD the wrong bytes, so this reads them from the
game's data folder itself. The music starts after about 2.4 s of silence (hence encode.ps1 -MusicStart 2.4).
The WAV is the game's own audio: keep it out of the repo (default output is Z:/Temp/claude/BigWalkMods/audio).
"""
import argparse, os, sys

import UnityPy
import fmod_toolkit

ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
ap.add_argument("--game", default="G:/SteamLibrary/steamapps/common/Big Walk")
ap.add_argument("--clip", default="Menu Music LOOP", help="start of the AudioClip's name")
ap.add_argument("--out", default="Z:/Temp/claude/BigWalkMods/audio/menu-theme.wav")
a = ap.parse_args()

data_dir = os.path.join(a.game, "Big Walk_Data")
env = UnityPy.load(os.path.join(data_dir, "data.unity3d"))
for obj in env.objects:
    if obj.type.name != "AudioClip":
        continue
    clip = obj.read()
    if not clip.m_Name.startswith(a.clip):
        continue
    if clip.m_AudioData:
        raw = bytes(clip.m_AudioData)
    else:
        r = clip.m_Resource
        with open(os.path.join(data_dir, os.path.basename(r.m_Source)), "rb") as f:
            f.seek(r.m_Offset)
            raw = f.read(r.m_Size)
    wavs = fmod_toolkit.raw_to_wav(raw, clip.m_Name, clip.m_Channels or 2, clip.m_Frequency or 48000, convert_pcm_float=True)
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "wb") as f:
        f.write(next(iter(wavs.values())))
    print("wrote %s (%.1f s) from '%s'" % (a.out, clip.m_Length, clip.m_Name))
    sys.exit(0)
print("no AudioClip named '%s...' in %s" % (a.clip, data_dir))
sys.exit(1)
