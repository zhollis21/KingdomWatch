# Releasing to Google Play

Publishing a GitHub Release tagged `vX.Y.Z` builds a signed Android App Bundle in CI and uploads it to the Play Console **internal** track. `promote-release.yml` then moves it up (internal → alpha → beta → production) by hand.

| Workflow | Does |
| --- | --- |
| `.github/workflows/android-release.yml` | Release published (or manual run): build Core, build the Unity project with GameCI, sign, upload to internal |
| `.github/workflows/promote-release.yml` | Manual run: promote the release on one track to a higher one |

The version name is the tag without the `v`. The version code is `YYMMDDNNN` (date plus run number), so it only ever goes up. The `bundleVersion` and `AndroidBundleVersionCode` in `ProjectSettings.asset` are overridden by the build and are only what a local build uses.

`Game/Assets/Editor/ReleaseBuild.cs` is the build. It sets the release options itself (no Development Build, no profiler, AAB, public symbols) instead of reading the Android build profile, which is the development profile.

## One-time setup

Do these once, in this order.

### Play Console

1. Create the app with package `com.RainbowSprinkles.KingdomWatch`. The package name is permanent.
2. Turn on Play App Signing. CI signs with an *upload* key; Google holds the app signing key.
3. **Upload the first bundle by hand** (internal testing) from a local build: the API cannot create a release for an app with none. Everything after that is automated.
4. Create a service account in Google Cloud, enable the Google Play Android Developer API, then in Play Console → Users and permissions invite the service account with release permissions for this app. Download its JSON key.

### Upload keystore

Make one and keep a backup outside the repository (never commit it):

```
keytool -genkeypair -v -keystore upload.keystore -alias kingdomwatch -keyalg RSA -keysize 2048 -validity 10000
base64 -w0 upload.keystore    # macOS: base64 -i upload.keystore
```

Register the upload certificate with Play (it is shown when you create the app with Play App Signing).

### Unity license (Personal)

GameCI activates with a license file. Follow [game-ci's activation guide](https://game.ci/docs/github/activation) to produce `Unity_lic.ulf` once; the secret is its full text. Personal licenses can expire or need re-activation: if a build fails with a licensing error, redo the activation and replace the secret.

### Art submodule

Create a fine-grained personal access token with **read-only Contents** access to `zhollis21/KingdomWatch-Art` only. The release build includes the art; check the pack's license allows shipping it inside a store build, which is a different question from keeping it out of this public repository.

### Repository secrets and variables

Settings → Secrets and variables → Actions.

| Name | Kind | Value |
| --- | --- | --- |
| `UNITY_LICENSE` | secret | Full text of the `.ulf` license file |
| `UNITY_EMAIL` | secret | Unity account email |
| `UNITY_PASSWORD` | secret | Unity account password |
| `ANDROID_KEYSTORE_BASE_64` | secret | Base64 of `upload.keystore` |
| `ANDROID_KEYSTORE_PASSWORD` | secret | Keystore and key password (the workflow uses one for both; use the same when creating the keystore) |
| `ANDROID_KEYSTORE_ALIAS` | variable | `kingdomwatch` (the alias you chose) |
| `GOOGLE_CLOUD_SERVICE_ACCOUNT_KEY` | secret | Full JSON key of the Play service account |
| `ART_REPO_TOKEN` | secret | The read-only token for the art repository |
| `ARTIFACT_PASSPHRASE` | secret | Any long random string; encrypts bundles that contain the art |

## The first upload, from a phone

Play needs the first bundle uploaded by hand. To get one without a computer:

1. Add the secrets above except `GOOGLE_CLOUD_SERVICE_ACCOUNT_KEY` and `ART_REPO_TOKEN` (GitHub in a phone browser, desktop site). A release build also needs `ARTIFACT_PASSPHRASE` (any long random string).
2. Actions → *Android Release Build & Deploy* → Run workflow. Enter a version; leave **deploy** and **include_art** off.
3. When it finishes, open the run and download the `kingdomwatch-…` artifact. It is a zip holding `KingdomWatch.aab`.
4. Unzip it in your phone's Files app, then upload the `.aab` in Play Console (Testing → Internal testing → Create release), in the desktop-site view of the browser.

That bundle is built **without the art**, on purpose: this repository is public and any signed-in GitHub user can download its workflow artifacts. A build that includes the art (every release, or a manual run with **include_art**) is encrypted with `ARTIFACT_PASSPHRASE` before it is stored, and only the deploy job decrypts it, so it cannot be downloaded and installed by hand.

## Cutting a release

1. Publish a GitHub Release tagged `vX.Y.Z`. Its body, trimmed to 500 characters, becomes the Play release notes.
2. Watch the run. The first Unity build is slow (a cold `Library`); later ones restore it from cache.
3. Test the internal build from the Play Store on a device.
4. Run *Promote Play Console Release* to move it up a track when ready.

To test the pipeline without a release, run *Android Release Build & Deploy* manually and enter a version.

## If it breaks

- **Licensing error:** replace `UNITY_LICENSE` (see above).
- **Unity version not found:** GameCI pulls the editor image named by `Game/ProjectSettings/ProjectVersion.txt`. A new Unity patch release can take a while to appear there; check the [image list](https://hub.docker.com/r/unityci/editor/tags).
- **No `KingdomWatch.Core.dll`:** the *Verify Core reached Unity* step failed, so Core did not build or its output path changed (`docs/unity.md`).
- **Version code already exists:** re-running a failed run reuses its run number, and so its version code. Start a fresh run instead.
- **Deploy fails but build passed:** missing Play permissions for the service account, no first manual upload, or an open draft edit in Play Console.
