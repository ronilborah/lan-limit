// Exercise the exact scripts emitted from the shipping C# source. No portal/account access.
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const dir = process.argv[2];
const snapshot = fs.readFileSync(`${dir}/snapshot.js`, 'utf8');
const login = fs.readFileSync(`${dir}/login.js`, 'utf8');
const credentials = JSON.parse(fs.readFileSync(`${dir}/credentials.json`, 'utf8'));
let rows = [['Resource', 'Allotted', 'Usage', 'Remaining'], ['Cycle total network traffic', '10000\u00a0MB', '0 MB', '7402.63 MB', '7402.63 MB', '2597.37\u00a0MB']];
let table = { querySelectorAll: () => rows.map(row => ({closest: () => table, children: row.map(text => ({tagName:'TD', innerText:text}))})) };
let texts = ['User portal for', 'test-user@bits-hyderabad.ac.in', 'Policy information'];
let fields = {}, clicked = 0;
const document = {
  body:{}, querySelectorAll: () => [table], getElementById: id => fields[id] || null,
  createTreeWalker: () => { let i = 0; return {currentNode:null, nextNode() { if(i >= texts.length) return false; this.currentNode = {parentElement:{tagName:'SPAN'},textContent:texts[i++]}; return true; }}; },
  querySelector: selector => selector === 'input[name="loginbutton"]' ? fields.button : null
};
const context = {document, NodeFilter:{SHOW_TEXT:4},getComputedStyle: () => ({display:'none'}),Event:class { constructor(type) {this.type=type;} }};
let page = vm.runInNewContext(snapshot,context);
assert.equal(page.Identity, texts[1]); assert.equal(page.Tables[0][1][5], rows[1][5]); assert.equal(page.Login,false);
fields = {username:{dispatchEvent(){}},password:{dispatchEvent(){}},button:{click(){clicked++;}}};
assert.equal(vm.runInNewContext(login,context),true);
assert.equal(fields.username.value,credentials.Username); assert.equal(fields.password.value,credentials.Password); assert.equal(clicked,1);
fields.button.disabled = true; assert.equal(vm.runInNewContext(login,context),false);
fields = {}; assert.equal(vm.runInNewContext(login,context),false);
console.log('Portal JavaScript checks passed: daily table extraction, identity, safe credential injection, disabled and missing login controls.');
