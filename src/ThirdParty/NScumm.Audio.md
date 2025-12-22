# Managing the NScumm.Audio Subtree

NScumm.Audio provides a pure C# (no native dependencies) emulator for the YM3812 / Adlib sound hardware.

- Original repo: https://github.com/scemino/NScumm.Audio
- Personal fork: https://github.com/BenMcLean/NScumm.Audio

> Note: NScumm.Audio is GPLv3. Keep license and attribution intact when using or redistributing.

This folder (`src/ThirdParty/NScumm.Audio`) is imported from the `NScumm.Audio` subfolder in the `BenMcLean/NScumm.Audio` fork using `git subtree`.

Git subtree allows us to:

- Vendor a specific folder from another repository
- Keep it up-to-date with minimal friction
- Avoid submodules or extra clone steps
- Squash history to keep our repository clean

## Fresh Clone Instructions

**If you just cloned this repository:** The NScumm.Audio code is already included in your clone as regular files. You can work with it normally - no special setup required.

**The rest of this document only matters if you want to:**
- Pull updates from the `BenMcLean/NScumm.Audio` fork
- Push your changes back to the `BenMcLean/NScumm.Audio` fork

To enable syncing with the `BenMcLean/NScumm.Audio` fork, you only need to add the remote (see next section). After adding the remote once, you can use the pull/push commands in the sections below whenever you want to sync.

### Configuring the remote (one-time setup for syncing)

If the `nscumm-audio` remote is not already set, run:

```
git remote add nscumm-audio https://github.com/BenMcLean/NScumm.Audio.git
git fetch nscumm-audio
```

Verify:

```
git remote -v
```

It should show something like:

```
nscumm-audio  https://github.com/BenMcLean/NScumm.Audio.git (fetch)
nscumm-audio  https://github.com/BenMcLean/NScumm.Audio.git (push)
```

## Pulling updates from the fork

**Why the split is necessary:** The `NScumm.Audio` repository contains a `NScumm.Audio` subfolder (same name as the repo) as well as other files and folders we don't need. We only need the hardware emulator, so we only want to import that specific subfolder, not the entire repository. `git subtree pull` by itself would pull everything, so we need to use `git subtree split` first to extract only the subfolder we want.

To pull new commits from the fork into the subtree:

```
git fetch nscumm-audio
git subtree split --prefix=NScumm.Audio nscumm-audio/master -b nscumm-audio-subtree
git subtree pull --prefix=src/ThirdParty/NScumm.Audio nscumm-audio-subtree --squash
git branch -D nscumm-audio-subtree
```

> The `--squash` flag keeps history simple in this repository.
> The `nscumm-audio-subtree` temporary branch is created during the split and deleted after the pull.

## Making changes and pushing back to the fork (optional)

This updates the fork with local changes:

```
git subtree split --prefix=src/ThirdParty/NScumm.Audio -b subtree-local
git push nscumm-audio subtree-local:master
git branch -D subtree-local
```

## Best practices

- Commit or stash other changes before pulling/pushing subtree updates.
- Only the folder under `src/ThirdParty/NScumm.Audio` is affected; everything else remains untouched.
