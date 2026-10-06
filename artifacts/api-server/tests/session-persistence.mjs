import assert from "node:assert/strict";
import {readFile,writeFile,unlink} from "node:fs/promises";

const filename = ".data/test-session.json";
const fixtures = JSON.parse(await readFile(".data/browser-fixtures.json","utf8"));
const cookies = new Map();
const merge = r => r.headers.getSetCookie().forEach(c=>{
  const [key,...value]=c.split(";")[0].split("="); cookies.set(key,value.join("="));
});
const cookieHeader = () => [...cookies].map(([k,v])=>`${k}=${v}`).join("; ");
async function csrf() {
  const r=await fetch(fixtures.origin+"/api/auth/csrf",{headers:{Cookie:cookieHeader()}});
  assert.equal(r.status,200); merge(r); return (await r.json()).token;
}
if (process.argv[2] === "before") {
  const token=await csrf();
  const r=await fetch(fixtures.origin+"/api/auth/login",{method:"POST",headers:{"Content-Type":"application/json","X-CSRF-TOKEN":token,Cookie:cookieHeader()},body:JSON.stringify(fixtures.parent)});
  assert.equal(r.status,200); merge(r);
  await writeFile(filename,JSON.stringify([...cookies]),{mode:0o600});
  console.log("Existing Identity session saved privately before key protection/restart.");
} else if (process.argv[2] === "after") {
  for(const [key,value] of JSON.parse(await readFile(filename,"utf8"))) cookies.set(key,value);
  const r=await fetch(fixtures.origin+"/api/auth/me",{headers:{Cookie:cookieHeader()}});
  assert.equal(r.status,200); assert.equal((await r.json()).email,fixtures.parent.email);
  const profile=await fetch(fixtures.origin+"/api/profile",{headers:{Cookie:cookieHeader()}});
  assert.equal(profile.status,200); assert.equal((await profile.json()).role,"parent");
  const token=await csrf();
  for(const path of ["forgot-password","resend-confirmation","confirm-email","reset-password"]) {
    const bad=await fetch(fixtures.origin+"/api/auth/"+path,{method:"POST",headers:{"Content-Type":"application/json","X-CSRF-TOKEN":token,Cookie:cookieHeader()},body:"{}"});
    assert.equal(bad.status,400,path+" rejects missing input");
  }
  await unlink(filename);
  console.log("PASS: existing cookie and profile ownership survived protected-key upgrade/restart; four invalid auth inputs return 400.");
} else throw new Error("Use before, then restart the API, then after (development only).");
