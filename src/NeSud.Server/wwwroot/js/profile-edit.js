"use strict";

const profileNotice = text => { $("profile-edit-status").textContent = text; };
async function loadProfileSettings() {
    const settings = await api("account/settings");
    if (tab !== "profile" || profileUser || guestMode) return;
    verifiedAccount = settings.verified === true;
    $("edit-username").value = settings.username;
    $("edit-username").readOnly = !verifiedAccount;
    $("verification-required").hidden = verifiedAccount;
    $("avatar-edit").disabled = !verifiedAccount;
    $("edit-email").value = settings.email || "";
    $("edit-email").readOnly = settings.verified;
    $("verify-controls").hidden = settings.verified;
    $("invite-controls").hidden = !settings.verified;
    $("avatar-remove").disabled = !verifiedAccount || !settings.hasAvatar;
    render();
    $("invite-count").textContent = "Підтверджених запрошень: " + settings.referrals;
    $("invite-link").value = settings.invite ? new URL("?invite=" + encodeURIComponent(settings.invite), location.origin + "/").href : "";
}
$("profile-edit").onsubmit = async event => {
    event.preventDefault();
    try {
        await api("account/settings", { username: $("edit-username").value, email: $("edit-email").value });
        await loadProfile();
        profileNotice("Збережено.");
    } catch (error) { profileNotice(error.message); }
};
$("verify-send").onclick = async () => {
    const button = $("verify-send");
    button.disabled = true;
    try {
        await api("account/verification/send", {});
        profileNotice("Код надіслано на пошту.");
    } catch (error) { profileNotice(error.message); }
    finally { button.disabled = false; }
};
$("verify-form").onsubmit = async event => {
    event.preventDefault();
    try {
        await api("account/verification", { code: $("verify-code").value.trim() });
        $("verify-code").value = "";
        await loadProfile();
        await refreshDaily();
        profileNotice("Пошту підтверджено.");
    } catch (error) { profileNotice(error.message); }
};
$("invite-copy").onclick = async () => {
    try { await navigator.clipboard.writeText($("invite-link").value); profileNotice("Посилання скопійовано."); }
    catch { $("invite-link").select(); profileNotice("Скопіюйте виділене посилання."); }
};

let avatarImage = null, avatarObjectUrl = null, avatarGeneration = 0;
const avatarStatus = text => { $("avatar-status").textContent = text; };
function clearAvatar() {
    avatarGeneration++;
    if (avatarObjectUrl) URL.revokeObjectURL(avatarObjectUrl);
    avatarObjectUrl = null;
    avatarImage = null;
    $("avatar-file").value = "";
    $("avatar-save").disabled = true;
    $("avatar-preview").getContext("2d").clearRect(0, 0, 256, 256);
}
function drawAvatar() {
    if (!avatarImage) return;
    const image = avatarImage, size = 256;
    const scale = Math.max(size / image.naturalWidth, size / image.naturalHeight) * Number($("avatar-zoom").value);
    const width = image.naturalWidth * scale, height = image.naturalHeight * scale;
    const context = $("avatar-preview").getContext("2d");
    context.clearRect(0, 0, size, size);
    context.drawImage(image, -(width - size) * Number($("avatar-x").value) / 100,
        -(height - size) * Number($("avatar-y").value) / 100, width, height);
}
$("avatar-edit").onclick = () => {
    if (!verifiedAccount) return;
    clearAvatar();
    avatarStatus("");
    $("avatar-dialog").showModal();
};
$("avatar-dialog").addEventListener("close", clearAvatar);
$("avatar-file").onchange = async () => {
    const file = $("avatar-file").files[0];
    if (!file) return;
    const generation = ++avatarGeneration;
    if (avatarObjectUrl) URL.revokeObjectURL(avatarObjectUrl);
    avatarObjectUrl = null;
    avatarImage = null;
    $("avatar-save").disabled = true;
    if (file.size > 5 * 1024 * 1024 || !/\.(jpe?g|png|svg)$/i.test(file.name)) {
        avatarStatus("Дозволено JPG, JPEG, PNG або SVG до 5 МБ.");
        return;
    }
    try {
        const mime = /\.svg$/i.test(file.name) ? "image/svg+xml" : /\.png$/i.test(file.name) ? "image/png" : "image/jpeg";
        avatarObjectUrl = URL.createObjectURL(new Blob([file], { type: mime }));
        const image = new Image();
        image.src = avatarObjectUrl;
        await image.decode();
        if (generation !== avatarGeneration) return;
        if (!image.naturalWidth || !image.naturalHeight || image.naturalWidth * image.naturalHeight > 40000000)
            throw Error("Завелика роздільна здатність.");
        avatarImage = image;
        $("avatar-zoom").value = 1;
        $("avatar-x").value = $("avatar-y").value = 50;
        drawAvatar();
        $("avatar-save").disabled = false;
        avatarStatus("Виберіть область за допомогою масштабу та зміщення.");
    } catch (error) { if (generation === avatarGeneration) avatarStatus(error.message || "Не вдалося прочитати зображення."); }
};
for (const id of ["avatar-zoom", "avatar-x", "avatar-y"]) $(id).oninput = drawAvatar;
$("avatar-save").onclick = async () => {
    if (!avatarImage || !verifiedAccount) return;
    $("avatar-save").disabled = true;
    try {
        const png = $("avatar-preview").toDataURL("image/png").split(",")[1];
        await api("account/avatar", { png });
        $("avatar-dialog").close();
        await loadProfile();
    } catch (error) { avatarStatus(error.message); $("avatar-save").disabled = false; }
};
$("avatar-remove").onclick = async () => {
    if (!verifiedAccount) return;
    try { await api("account/avatar", { png: null }); await loadProfile(); }
    catch (error) { profileNotice(error.message); }
};
