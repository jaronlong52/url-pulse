document.addEventListener("DOMContentLoaded", () => {
  document.querySelectorAll(".utc-time").forEach((el) => {
    const utc = el.getAttribute("datetime");
    el.textContent = new Date(utc).toLocaleString();
  });
});
