#!/usr/bin/env bash
set -euo pipefail

case "$GITHUB_REF_NAME" in
  "release/v$VERSION") tag="v$VERSION"; latest=true; prerelease=false; kind="stable release" ;;
  "native-preview/v$VERSION") tag="v$VERSION-preview"; latest=false; prerelease=true; kind="preview" ;;
  *) echo "Unexpected publication branch." >&2; exit 1 ;;
esac

archive="Plainix-$VERSION-Windows-x64.zip"
(cd installer && zip -qr "../$archive" .)
unzip -tq "$archive"
(cd native-installer && sha256sum -- *) > SHA256SUMS.txt
sha256sum "$archive" >> SHA256SUMS.txt
cert_sha=$(sha256sum native-installer/Plainix.cer | cut -d ' ' -f 1)
base="https://github.com/$GITHUB_REPOSITORY/releases/download/$tag"
cat > notes.md <<EOFNOTES
[**Download Plainix.appinstaller for Windows x64**]($base/Plainix.appinstaller)

1. On first use of this signing certificate, [download Plainix.cer]($base/Plainix.cer). Verify the file's SHA-256 against the value below. Open the certificate, choose **Install Certificate → Local Machine → Place all certificates in the following store → Trusted People**, and finish the Windows administrator prompt.
2. Download and double-click **Plainix.appinstaller**. Choose **Install** or **Update** in Windows App Installer. Windows downloads the signed app and its required frameworks.
3. Open **Plainix** from Start. This build retains the fork's existing package identity and settings.

SHA-256 of the public certificate: \`$cert_sha\`.

This release uses a **self-signed certificate**, not a SignPath Foundation certificate. A clean Windows installation does not trust this signer by default. See the [Code signing policy](https://github.com/$GITHUB_REPOSITORY/blob/master/docs/code-signing-policy.md) and [privacy policy](https://github.com/$GITHUB_REPOSITORY/blob/master/PRIVACY.md).\n\nThe ZIP under **Assets** contains the complete offline installer, including the SDK scripts. SHA256SUMS.txt covers the ZIP and native installation files.
EOFNOTES
if [[ "$kind" == "preview" ]]; then
  echo >> notes.md
  echo 'This preview uses its own certificate and a version-specific feed. Stable builds use a separate update feed.' >> notes.md
else
  echo >> notes.md
  echo 'The stable feed checks for newer releases when Plainix starts.' >> notes.md
fi

# GitHub serves uploaded asset MIME types to Windows App Installer.
# A draft keeps its descriptor private until all referenced files are present.
api="repos/$GITHUB_REPOSITORY/releases"
existing=$(gh api --paginate "$api" --jq ".[] | select(.tag_name == \"$tag\") | [.id, .draft, .target_commitish] | @tsv")
if [[ -n "$existing" ]]; then
  read -r id draft commit <<< "$existing"
  [[ "$draft" == true && "$commit" == "$GITHUB_SHA" ]] || { echo "A different or published release already owns $tag." >&2; exit 1; }
else
  id=$(gh api --method POST "$api" --field "tag_name=$tag" --field "target_commitish=$GITHUB_SHA" \
    --field "name=Plainix $VERSION ($kind)" --field 'body=@notes.md' --field draft=true --field "prerelease=$prerelease" --jq .id)
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
  digest="sha256:$(sha256sum "$file" | cut -d ' ' -f 1)"
  jq -e --arg digest "$digest" --arg name "$name" --arg mime "$mime" --argjson bytes "$(stat -c %s "$file")" \
    '.state == "uploaded" and .name == $name and .size == $bytes and .content_type == $mime and .digest == $digest' <<< "$result" >/dev/null
done
gh api --method PATCH "$api/$id" --field 'body=@notes.md' --field draft=false \
  --field "prerelease=$prerelease" --raw-field "make_latest=$latest" --jq .html_url
