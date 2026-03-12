import subprocess
import sys

# === CẤU HÌNH ===
GITHUB_REPO    = "quanq026/ElanAP"
GIT_REMOTE     = "origin"
GIT_BRANCH     = "main"

TAG            = "v3.1.1"
RELEASE_TITLE  = "V3.1.1"
RELEASE_NOTES  = "Update automatic release script"
COMMIT_MESSAGE = "Update automatic release script"
# =================

def run(cmd):
    result = subprocess.run(cmd, shell=True, capture_output=True, text=True)
    if result.stdout.strip():
        print(result.stdout.strip())
    if result.returncode != 0:
        print(f"ERROR: {result.stderr.strip()}")
        sys.exit(1)

def main():
    tag   = TAG
    notes = RELEASE_NOTES
    msg   = COMMIT_MESSAGE

    if not tag.startswith("v"):
        print("Tag phải bắt đầu bằng 'v'")
        sys.exit(1)

    print("\n--- Staging ---")
    run("git add -A")

    print("--- Committing ---")
    run(f'git commit -m "{msg}"')

    print("--- Tagging ---")
    run(f'git tag -a {tag} -m "{notes}"')

    print("--- Pushing ---")
    run(f"git push {GIT_REMOTE} {GIT_BRANCH} --tags")

    print(f"\nDone! GitHub Actions dang chay, kiem tra tai:")
    print(f"  https://github.com/{GITHUB_REPO}/actions")

if __name__ == "__main__":
    main()
