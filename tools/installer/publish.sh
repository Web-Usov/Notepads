#!/usr/bin/env bash
set -euo pipefail

case "$GITHUB_REF_NAME" in
  "release/v$VERSION") tag="v$VERSION"; publish_flags=(--latest); kind="stable release" ;;
  "native-preview/v$VERSION") tag="v$VERSION-preview"; publish_flags=(--prerelease --latest=false); kind="preview" ;;
  *) echo "Unexpected publication branch." >&2; exit 1 ;;
esac

archive="Notepads-$VERSION-Windows-x64.zip"
(cd installer && zip -qr "../$archive" .)
unzip -tq "$archive"
(cd native-installer && sha256sum -- *) > SHA256SUMS.txt
sha256sum "$archive" >> SHA256SUMS.txt
cert_sha=$(sha256sum native-installer/Notepads.cer | cut -d ' ' -f 1)
base="https://github.com/$GITHUB_REPOSITORY/releases/download/$tag"
cat > notes.md <<EOFNOTES
[**Download Notepads.appinstaller for Windows x64**]($base/Notepads.appinstaller)

1. On first use of this signing certificate, [download Notepads.cer]($base/Notepads.cer). Verify the file's SHA-256 against the value below. Open the certificate, choose **Install Certificate → Local Machine → Place all certificates in the following store → Trusted People**, and finish the Windows administrator prompt.
2. Download and double-click **Notepads.appinstaller**. Choose **Install** or **Update** in Windows App Installer. Windows downloads the signed app and its required frameworks.
3. Open **Notepads-Dev** from Start. This build retains the fork's existing package identity and settings.

SHA-256 of the public certificate: \`$cert_sha\`.

The ZIP under **Assets** contains the complete offline installer, including the SDK scripts. SHA256SUMS.txt covers the ZIP and native installation files.
EOFNOTES
if [[ "$kind" == "preview" ]]; then
  echo >> notes.md
  echo 'This preview uses its own certificate and a version-specific feed. Stable builds use a separate update feed.' >> notes.md
else
  echo >> notes.md
  echo 'The stable feed checks for newer releases when Notepads starts.' >> notes.md
fi

# GitHub serves uploaded asset MIME types to Windows App Installer.
# A draft keeps its descriptor private until all referenced files are present.
api="repos/$GITHUB_REPOSITORY/releases"
existing=$(gh api --paginate "$api" --jq ".[] | select(.tag_name == \"$tag\") | [.id, .draft, .target_commitish] | @tsv")
if [[ -n "$existing" ]]; then
  read -r id draft commit <<< "$existing"
  [[ "$draft" == true && "$commit" == "$GITHUB_SHA" ]] || { echo "A different or published release already owns $tag." >&2; exit 1; }
else
  gh release create "$tag" --repo "$GITHUB_REPOSITORY" --target "$GITHUB_SHA" --draft --title "Notepads $VERSION ($kind)" --notes-file notes.md
  id=$(gh api --paginate "$api" --jq ".[] | select(.tag_name == \"$tag\" and .draft) | .id")
fi
[[ "$id" =~ ^[0-9]+$ ]]
for file in native-installer/* "$archive" SHA256SUMS.txt; do
  name=${file##*/}
  case "$name" in
    *.appinstaller) mime=application/appinstaller ;;
    *.msixbundle) mime=application/msixbundle ;;
    *.appx) mime=application/appx ;;
    *.cer) mime=application/pkix-cert ;;
    *.zip) mime=application/zip ;;
    SHA256SUMS.txt) mime=text/plain ;;
    *) echo "Unexpected release asset: $name" >&2; exit 1 ;;
  esac
  previous=$(gh api --paginate "$api/$id/assets" --jq ".[] | select(.name == \"$name\") | .id")
  if [[ -n "$previous" ]]; then gh api --method DELETE "$api/assets/$previous"; fi
  result=$(curl --silent --show-error --fail-with-body --request POST \
    --header "Authorization: Bearer $GH_TOKEN" --header "Content-Type: $mime" --data-binary "@$file" \
    "https://uploads.github.com/repos/$GITHUB_REPOSITORY/releases/$id/assets?name=$name")
  jq -e --arg name "$name" --arg mime "$mime" --argjson bytes "$(stat -c %s "$file")" \
    '.state == "uploaded" and .name == $name and .size == $bytes and .content_type == $mime' <<< "$result" >/dev/null
done
gh release edit "$tag" --repo "$GITHUB_REPOSITORY" --notes-file notes.md --draft=false "${publish_flags[@]}"
