window.guru = {
  csrf: null,
  async request(path, method = "GET", body = null) {
    if (!path.startsWith("/") || path.startsWith("//")) throw new Error("Invalid API path");
    if (method !== "GET" && !this.csrf) {
      const r = await fetch("/api/auth/csrf", { credentials: "same-origin" });
      if (!r.ok) throw new Error("Unable to initialise secure request");
      this.csrf = (await r.json()).token;
    }
    const r = await fetch("/api" + path, {
      method, credentials: "same-origin",
      headers: { "Content-Type": "application/json", ...(method === "GET" ? {} : { "X-CSRF-TOKEN": this.csrf }) },
      body: body == null ? undefined : JSON.stringify(body)
    });
    if (r.ok && (path === "/auth/login" || path === "/auth/logout")) this.csrf = null;
    return { status: r.status, body: r.status === 204 ? null : await r.json() };
  },
  fileInfo(id) {
    const f = document.getElementById(id)?.files?.[0];
    return f ? { name: f.name, size: f.size, type: f.type } : null;
  },
  async upload(id) {
    const file = document.getElementById(id)?.files?.[0];
    if (!file) throw new Error("Choose a file first.");
    const result = await this.request("/storage/uploads/request-url", "POST", { name: file.name, size: file.size, contentType: file.type });
    if (result.status >= 400) throw new Error(result.body.error);
    const r = await fetch(result.body.uploadURL, { method: "PUT", headers: { "Content-Type": file.type }, body: file });
    if (!r.ok) throw new Error("File upload failed. Please try again.");
    return result.body.objectPath;
  }
};
