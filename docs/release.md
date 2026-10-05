# Releasing to Google Play

Publishing a GitHub Release tagged `vX.Y.Z` builds a signed Android App Bundle in CI and uploads it to the Play Console **internal** track. `promote-release.yml` then moves it up (internal → alpha → beta → production) by hand.

| Workflow | Does |
| --- | --- |
| `.github/workflows/android-release.yml` | Release published (or manual run): build Core, build the Unity project with GameCI, sign, upload to internal. A pull request that changes the workflow runs the same build as version `0.0.0-pr.N`, without the upload |
| `.github/workflows/promote-release.yml` | Manual run: promote the newest completed release on one track to a higher one |

The version name is the tag without the `v`. The version code is `YYDDDHHMM` (UTC year, day of year, hour and minute the build started), so it only ever goes up and a re-run gets a new one. The `bundleVersion` and `AndroidBundleVersionCode` in `ProjectSettings.asset` are overridden by the build and are only what a local build uses.

The Unity build uses GameCI's own build script, configured entirely in the workflow: an App Bundle, no Development Build, no profiler, public debug symbols. Unity writes the symbols beside the bundle as `…-IL2CPP.symbols.zip`; the upload sends both, so Play can name the functions in native crash reports. The committed Android build profile (the development one, see `docs/unity.md`) is not used, because no profile is active on a fresh checkout.

Every build includes the art from the private submodule. The bundle is the game players install, so the workflow keeps it as a downloadable artifact for five days; the raw art files never leave the submodule. There is no Unity `Library` cache, so each build imports from scratch, about 18 minutes in Unity. A cache would save roughly 8–10 of them, but releases could only restore one saved on `main`, which nothing keeps fresh; for occasional releases that run unattended, it is not worth the extra moving parts.

## One-time setup

Do these once, in this order.

### Play Console

1. Create the app (done). Its package name is fixed by the first bundle uploaded: `com.RainbowSprinkles.KingdomWatch`, from `ProjectSettings.asset`.
2. Use Play App Signing (the default). CI signs with an *upload* key; Google holds the app signing key.
3. **Upload the first bundle by hand and roll it out** to internal testing (see below). The API cannot create the app's first release, and a draft app accepts only draft releases from it.
4. Create a service account in Google Cloud, enable the Google Play Android Developer API, then in Play Console → Users and permissions invite the service account with release permissions for this app. Download its JSON key.

### Upload keystore

Make one and keep a backup outside the repository (never commit it). Use one password for the keystore and the key; the workflow passes the same one for both.

```
keytool -genkeypair -v -keystore upload.keystore -alias kingdomwatch -keyalg RSA -keysize 2048 -validity 10000
base64 -w0 upload.keystore    # macOS: base64 -i upload.keystore
```

The first bundle uploaded by hand registers this key as the upload key.

### Unity license (Personal)

GameCI needs the `Unity_lic.ulf` file that Unity Hub writes when a Personal license is activated on a computer signed in to your Unity account. It cannot be generated in CI: [GameCI's activation guide](https://game.ci/docs/github/activation) says to install Unity Hub, sign in, add a free personal license (Preferences → Licenses → Add), then copy the file from `C:\ProgramData\Unity\Unity_lic.ulf` (Windows), `/Library/Application Support/Unity/Unity_lic.ulf` (macOS) or `~/.local/share/unity3d/Unity/Unity_lic.ulf` (Linux). A license activated on one operating system works for builds on another. The secret `UNITY_LICENSE` is the full text of that file. If a build later fails with a licensing error, activate again and replace the secret.

### Art submodule

CI reads the art with a read-only deploy key of `zhollis21/KingdomWatch-Art` (its Settings → Deploy keys, "KingdomWatch release build"), whose private half is the secret `ART_REPO_SSH_KEY`. A deploy key opens only its own repository and does not expire. To replace it:

```
ssh-keygen -t ed25519 -N "" -f art_key
gh api repos/zhollis21/KingdomWatch-Art/keys -f title="KingdomWatch release build" -f key="$(cat art_key.pub)" -F read_only=true
gh secret set ART_REPO_SSH_KEY -R zhollis21/KingdomWatch < art_key
rm art_key art_key.pub
```

Check the pack's license allows shipping it inside a store build, which is a different question from keeping it out of this public repository.

### Repository secrets and variables

Settings → Secrets and variables → Actions.

| Name | Kind | Value |
| --- | --- | --- |
| `UNITY_LICENSE` | secret | Full text of the `.ulf` license file |
| `UNITY_EMAIL` | variable | Unity account email |
| `UNITY_PASSWORD` | secret | Unity account password |
| `ANDROID_KEYSTORE_BASE_64` | secret | Base64 of `upload.keystore` |
| `ANDROID_KEYSTORE_PASSWORD` | secret | Keystore and key password |
| `ANDROID_KEYSTORE_ALIAS` | variable | `kingdomwatch` (the alias you chose) |
| `ART_REPO_SSH_KEY` | secret | Private half of the art repository's read-only deploy key |
| `GOOGLE_CLOUD_SERVICE_ACCOUNT_KEY` | secret | Full JSON key of the Play service account |

## The first upload

1. Add everything above. The first build does not use `GOOGLE_CLOUD_SERVICE_ACCOUNT_KEY`, but every upload after it does.
2. Actions → *Android Release Build & Deploy* → Run workflow. Enter a version; leave **deploy** off.
3. When it finishes, open the run and download the `kingdomwatch-…` artifact, a zip holding `KingdomWatch.aab` and the symbols zip. Unzip it.
4. In Play Console: Testing → Internal testing → Create release, upload the `.aab`, then **roll it out**. Play also asks for the app's content declarations (Policy → App content) before the first rollout.

After that, a published GitHub Release (or a manual run with **deploy** on) uploads on its own.

## Cutting a release

1. Publish a GitHub Release tagged `vX.Y.Z`. Its body, trimmed to 500 characters, becomes the Play release notes.
2. Watch the run.
3. Test the internal build from the Play Store on a device.
4. Run *Promote Play Console Release* (or use **Promote release** in Play Console) to move it up a track when ready. Promoting to production rolls out to 100%.

## If it breaks

- **Licensing error:** replace `UNITY_LICENSE` (see above).
- **Unity version not found:** GameCI pulls the editor image named by `Game/ProjectSettings/ProjectVersion.txt`. A new Unity patch release can take a while to appear there; check the [image list](https://hub.docker.com/r/unityci/editor/tags).
- **No `KingdomWatch.Core.dll`:** the *Verify Core reached Unity* step failed, so Core did not build or its output path changed (`docs/unity.md`).
- **Checkout art fails ("repository not found"):** `ART_REPO_SSH_KEY` is missing, or the deploy key was removed from the art repository.
- **A release never ran:** builds that upload run one at a time, and GitHub keeps only one waiting. Publishing a third release while one builds and another waits cancels the waiting one; run it again by hand.
- **Upload fails but the build passed:** missing Play permissions for the service account, the first release not uploaded and rolled out by hand, or an open draft edit in Play Console.
- **Play rejects the target API level:** the build targets the highest Android SDK in GameCI's image. Set `androidTargetSdkVersion` on the Unity build step (e.g. `AndroidApiLevel36`) to pin it.
