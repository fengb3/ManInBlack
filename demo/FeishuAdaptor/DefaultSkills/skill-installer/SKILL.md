---
name: skill-installer
description: Install .skill files by extracting them into the project. Use this skill whenever the user uploads, shares, or references a .skill file, wants to install a skill, or says something like "install this skill", "add this skill", or "load this .skill file". Also trigger when the user drags a .skill file into the conversation.
---

# Skill Installer

You install `.skill` or `.zip` or `.gzip` files into the current project's `.agents/skills/` directory.

## What is a .skill file?

A `.skill` file is a renamed `.zip` or `.gzip` archive containing an agent skill (at minimum a `SKILL.md` file, plus optional `scripts/`, `references/`, `assets/` directories).

## Installation flow

### 1. Locate the .skill file

The file may be:
- Uploaded directly into the conversation
- Located at a path the user provides
- In the downloads folder (check for recent `.skill` files if the user just downloaded one)

### 2. Determine the skill name

Extract the skill name from the archive's top-level directory structure. Most `.skill` files contain a single root folder — use that folder name as the skill name. If there's no root folder (files are at the archive root), use the `.skill` filename (without extension) as the skill name.

### 3. Check for existing installation

Check if `.agents/skills/<skill-name>/` already exists in the working directory. If it does:

- Ask the user: "The skill `<skill-name>` is already installed. Do you want to overwrite it with the new version?"
- Only proceed if they confirm

### 4. Extract

Run the extraction:

```bash
mkdir -p .agents/skills
unzip -o <skill-file> -d .agents/skills/
```

If the archive contains a root folder, it will naturally extract to `.agents/skills/<skill-name>/`. If files are at the archive root, create the target directory first:

```bash
mkdir -p .agents/skills/<skill-name>
unzip -o <skill-file> -d .agents/skills/<skill-name>/
```

### 5. Verify

After extraction, verify the installation by checking:
- `.agents/skills/<skill-name>/SKILL.md` exists
- Read the `SKILL.md` frontmatter to confirm the skill name and description

### 6. Confirm to the user

Report the installed skill's name, description, and location. For example:

> Installed skill `my-skill` to `.agents/skills/my-skill/`. It provides [description from SKILL.md]. Reset the conversation to make the new skill available to use.

## Error handling

- If the file is not a valid zip, tell the user and suggest re-downloading
- If `SKILL.md` is missing after extraction, warn the user — the archive may not be a valid skill
- If the target directory path contains spaces or special characters, quote all paths
