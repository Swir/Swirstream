# Swir TasteNet, preference learning and search learning — 0.9.7

The shipping UI has no assistant or command box. It loads model/tastenet.json.
No LLM, remote inference, speech recognition or API key is used.

## Topic classifier
Trained from scratch on 316 project-authored short phrases in Polish, English,
German, French and Spanish. Ten classes: news, music, sports, movies, kids,
documentary, entertainment, lifestyle, religious, general. Training data and
40 independent, project-authored evaluation phrases are included.
Architecture reuses the IntentNet implementation: 1024 hashed word/bigram/character
features, 64 tanh units, 10 softmax outputs; SGD, 100 epochs, seed 4019.
The internal serialization format is SwirIntentNet-1, with a ten-label shape check.

On the 40 evaluation phrases: 39 top-label matches, 38 accepted predictions,
38 accepted correct predictions, 2 abstentions. Acceptance requires score >= .62,
margin >= .25 and a vocabulary match. These scores are not calibrated probabilities.
This is a small synthetic evaluation, not a field benchmark of world TV channels.
Unknown names can be unclassifiable or wrong. Explicit source category tags take
precedence over inference, and low confidence is shown as Bez kategorii.

Reproduce training on Windows:
    SwirStreamAI.exe --train --input model/taste-training.json --output model/tastenet.json
The training command exits without UI or network. The seed and algorithm are fixed.

## Per-profile preference learning
A separate online logistic regression has 512 hashed features: topic, country,
media kind, channel identity and name tokens. Feature vector L2-normalized.
Weights start at zero. Positive/negative feedback updates weights by gradient
ascent on log likelihood, rate .8, decay .9995, bounds [-8,8]. No hidden server.
Signals: favorite added; 30 seconds of decoded playback progress; channel hidden.
In0.9.5 playback confirmation requires progress of selected audio and TV video;
genuine video-only content does not require audio. Encrypted playback requires
12 seconds of sustained progress before positive history/source/watch signals.
A decoder failure or moving timeline alone cannot provide a positive label.
The same channel/signal is accepted at most once per 24 h. Pauses and stalls do
not accrue watch time; elapsed wall time per tick is capped at two seconds.
Test fixtures marked Test do not train preferences. Learning can be disabled/reset.

The learned probability contributes up to +/-12 ranking points. Explicit preferred
topics contribute 14 points. Neither is a percentage chance of stream availability.
A freshly verified working source is ordered ahead of unknown/failed sources.
When preferred topics are present, verification targets that pool first.

Each profile's weights/signals live in its library.json. Other profile libraries are
separate. Catalog caches and source health are shared. Profiles are not secured
accounts. Favorite/history/playlist/profile data is not transmitted for training.
Search queries may include chosen topics and countries, which go to the search engine.

## Per-profile search learning (introduced in 0.7.0)

A third local model is an online logistic regression that helps order broadcaster
pages in the next web search. It uses 512 normalized hashed features for site,
channel identity, country, topic and media kind. Weights start at zero; accepted
playback outcomes update them with learning rate .55, per-update decay .9995,
bounds [-6,6] and an exponential time decay with a 30-day time constant.
The fixed TasteNet classifier itself is not retrained during use.

Labels come from actual playback probes and player outcomes, including failures.
Accepted records require a public stream, positive attempt duration and a recent
timestamp (up to ten minutes old, at most one minute ahead for clock tolerance).
An HTTP response, downloaded playlist or listed channel is not a success label.
The same source/outcome is deduplicated for 30 minutes; a newer opposite outcome
can correct it immediately. Local test material and browser-only entries are excluded.

The score snapshot combines existing taste/preference scoring with a bounded
availability-model contribution, gradually enabled over the first six examples.
It changes crawl order, not membership or playback verification status. Exact
channel requests and selected countries remain constraints. Every fourth crawl
slot explores least-visited pages, with country rotation to avoid one country
consuming the limited scan. A running scan uses an immutable score snapshot.
These are practical safeguards, not evidence of measured search-quality gains.

State lives in search-learning.json inside that profile's folder. It contains
512 weights, timestamps/counts and at most 4,000 hashed source fingerprints kept
for 30 days; it does not store raw names or URLs. This is not a claim that hashes
are anonymous. The separate profile-local visited-broadcasters.json contains crawl
history, while library.json retains preferences and the user's library as before.
No model state is sent to cloud inference or a training service.

The shared preference checkbox disables new learning and use of learned scores;
it also disables personal crawl-order bias. Explicit country/topic filters remain.
Turning it off retains stored state. Reset clears preference weights/signals,
search-learning state and the active profile's crawl history; it does not erase
favorites, viewing history, playlists or another profile. Back up all of data to
retain both models and all profiles. --search-learning-test exercises these rules
with controlled fixtures; it is not a real-world improvement benchmark.

## Retired command model
model/intentnet.json and the original 19-class training/evaluation set remain only
for legacy regression tests and reproducibility. The normal UI does not load or
execute that command model and cannot receive assistant instructions.

## Search and playback
Network discovery, playlist parsing, health probes and fallback are explicit C#
logic. The models classify/rank; they do not invent URLs or certify broadcasting
rights. A short playback probe cannot guarantee hours of stability.

## Application changes through 0.8.0
Fixed TasteNet weights and its training/evaluation data are unchanged. Channel aliases,
identity matching, parallel providers, embedded-player extraction and playback
verification are deterministic application logic, not generated model output.
Provider counts are not accuracy estimates and source metadata is not proof
that a stream is available or carries the expected program indefinitely.
The startup gate, playlist/EPG handling and embedded mpv/FFmpeg player introduced in 0.7.0 do not
alter the fixed classifier weights. The existing search-learning model updates locally
from playback outcomes as described above. Playback proof comes from actual
decoder tests, not a model prediction or playlist download.

## Public playlist ranking in 0.9.1
The module reuses SearchLearning.Snapshot (including TasteNet preference scores).
It orders playlists by unique matching programmes, explicit countries, freshness,
recent decoder confirmations and average per-programme preference. Duplicate
addresses do not add programme votes. Disabling preference learning gives a zero
personal score; no playlist is hidden for having no history. Sorting does not
write timestamps, modify model weights or create playback confirmations.
The public M3U adapter now accepts bounded direct public .m3u/.m3u8 results beyond
GitHub, with the same content, freshness and network checks. It never generates
operator accounts, registered MACs or DRM licences. Publicly exposed account
entries are removed before caching and on every refresh of an AI-added subscription.
