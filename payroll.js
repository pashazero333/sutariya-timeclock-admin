function timesheetDateValue(d){
  const z=n=>String(n).padStart(2,'0');
  return `${d.getFullYear()}-${z(d.getMonth()+1)}-${z(d.getDate())}`;
}

function timesheetDateFromInput(value){
  const m=String(value||'').match(/^(\d{4})-(\d{2})-(\d{2})$/);
  if(!m)return null;
  return new Date(Number(m[1]),Number(m[2])-1,Number(m[3]),12,0,0,0);
}

function getTimesheetRange(){
  const mode=document.getElementById('timesheetRangeMode')?.value||'period';
  if(mode==='custom'){
    const start=timesheetDateFromInput(document.getElementById('timesheetCustomStart')?.value);
    const end=timesheetDateFromInput(document.getElementById('timesheetCustomEnd')?.value);
    if(!start||!end)return null;
    return {start,end,custom:true};
  }
  const per=selectedPeriod('payrollPeriod');
  return {start:new Date(per.start),end:new Date(per.end),custom:false};
}

function timesheetEmployeeId(){
  const v=document.getElementById('timesheetEmployee')?.value||'';
  return v?Number(v):null;
}

function timesheetRows(){
  const range=getTimesheetRange();
  if(!range)return [];
  const end=new Date(range.end);end.setHours(23,59,59,999);
  const employeeId=timesheetEmployeeId();
  return punches.filter(p=>{
    const t=new Date(p.clock_in);
    return t>=range.start&&t<=end&&(!employeeId||Number(p.employee_id)===employeeId);
  });
}

function payrollData(){
  const rows=timesheetRows();
  const m=new Map();
  for(const p of rows){
    if(!m.has(p.employee_id))m.set(p.employee_id,{id:p.employee_id,raw:0,rounded:0,shifts:[]});
    const x=m.get(p.employee_id);
    x.shifts.push(p);
    if(p.clock_out){
      x.raw+=Number(p.raw_hours||0);
      x.rounded+=Number(p.rounded_hours||0);
    }
  }
  return [...m.values()].sort((a,b)=>empName(a.id).localeCompare(empName(b.id)));
}

function payrollDay(v){
  return new Date(v).toLocaleDateString('en-US',{weekday:'short',month:'short',day:'numeric',year:'numeric'});
}

function payrollTime(v){
  return v?new Date(v).toLocaleTimeString('en-US',{hour:'numeric',minute:'2-digit'}):'';
}

function isStaleOpenShift(p){
  if(p.clock_out)return false;
  return Date.now()-new Date(p.clock_in).getTime()>18*60*60*1000;
}

function openShiftBadge(p){
  if(p.clock_out)return payrollTime(p.clock_out);
  return isStaleOpenShift(p)?'<span class="pill amber">OPEN</span>':'<span class="pill green">CLOCKED IN</span>';
}

function shiftAuditBadges(p){
  const meta=typeof punchAuditMeta==='function'?punchAuditMeta(p.id):{count:0,edits:0,manual:false};
  const badges=[];
  if(p.source==='admin_manual'||meta.manual)badges.push('<span class="audit-badge manual-badge">Manual</span>');
  if(meta.edits>0)badges.push('<span class="audit-badge edited-badge">Edited</span>');
  return badges.length?`<div class="shift-badges">${badges.join('')}</div>`:'';
}

function historyButtonLabel(p){
  const meta=typeof punchAuditMeta==='function'?punchAuditMeta(p.id):{count:0};
  return meta.count>0?`History (${meta.count})`:'History';
}

function togglePayrollEmployee(id){
  const row=document.getElementById('payroll-detail-'+id);
  const btn=document.getElementById('payroll-toggle-'+id);
  if(!row||!btn)return;
  const opening=row.classList.contains('hidden');
  row.classList.toggle('hidden',!opening);
  btn.querySelector('.toggle-arrow').textContent=opening?'▼':'▶';
  btn.setAttribute('aria-expanded',opening?'true':'false');
}

function syncTimesheetEmployeeOptions(){
  const select=document.getElementById('timesheetEmployee');
  if(!select)return;
  const current=select.value;
  select.innerHTML='<option value="">All employees</option>'+employees.map(e=>`<option value="${e.id}">${e.name}</option>`).join('');
  if([...select.options].some(o=>o.value===current))select.value=current;
}

function updateTimesheetRangeControls(){
  const custom=document.getElementById('timesheetRangeMode')?.value==='custom';
  document.getElementById('timesheetPeriodField')?.classList.toggle('hidden',custom);
  document.getElementById('timesheetCustomStartField')?.classList.toggle('hidden',!custom);
  document.getElementById('timesheetCustomEndField')?.classList.toggle('hidden',!custom);
  const btn=document.getElementById('loadPayroll');
  if(btn)btn.textContent='Apply';
  if(custom){
    const per=selectedPeriod('payrollPeriod');
    const start=document.getElementById('timesheetCustomStart');
    const end=document.getElementById('timesheetCustomEnd');
    if(start&&!start.value)start.value=timesheetDateValue(per.start);
    if(end&&!end.value)end.value=timesheetDateValue(per.end);
  }
}

function payrollPayday(end){
  const d=new Date(end);d.setDate(d.getDate()+7);return d;
}

function timesheetRangeText(){
  const range=getTimesheetRange();
  if(!range)return 'Choose a valid start and end date.';
  const who=timesheetEmployeeId()?empName(timesheetEmployeeId()):'All employees';
  if(range.custom)return `Custom date range · ${fmtShort(range.start)} – ${fmtShort(range.end)} · ${who}`;
  return `Payroll period · ${fmtShort(range.start)} – ${fmtShort(range.end)} · Payday: ${fmtShort(payrollPayday(range.end))} · ${who}`;
}

function syncTimesheetVisibility(){
  const card=document.getElementById('payrollBody')?.closest('.card.table-card');
  const page=document.getElementById('page-payroll');
  if(card&&page)card.style.display=page.classList.contains('active')?'':'none';
}

function renderPayroll(){
  syncTimesheetEmployeeOptions();
  updateTimesheetRangeControls();
  syncTimesheetVisibility();
  const range=getTimesheetRange();
  const label=document.getElementById('timesheetRangeLabel');
  if(label)label.textContent=timesheetRangeText();
  if(!range||range.end<range.start){
    payrollBody.innerHTML='<tr><td colspan="4" class="empty">End date must be on or after the start date.</td></tr>';
    return;
  }
  const data=payrollData();
  if(!data.length){
    payrollBody.innerHTML='<tr><td colspan="4" class="empty">No time entries in this date range.</td></tr>';
    return;
  }
  const raw=data.reduce((a,x)=>a+x.raw,0);
  const rounded=data.reduce((a,x)=>a+x.rounded,0);
  const shifts=data.reduce((a,x)=>a+x.shifts.length,0);
  const employeeRows=data.map(x=>{
    const open=x.shifts.filter(p=>!p.clock_out);
    const stale=open.filter(isStaleOpenShift);
    const statusBadge=stale.length?`<span class="pill amber employee-open-badge">${stale.length} OPEN</span>`:(open.length?'<span class="pill green employee-open-badge">CLOCKED IN</span>':'');
    const detail=x.shifts.slice().sort((a,b)=>new Date(a.clock_in)-new Date(b.clock_in)).map(p=>`
      <tr>
        <td>${payrollDay(p.clock_in)}</td>
        <td>${payrollTime(p.clock_in)}${shiftAuditBadges(p)}</td>
        <td>${openShiftBadge(p)}</td>
        <td>${p.clock_out?hrs(p.raw_hours):''}</td>
        <td>${p.clock_out?`<strong>${hrs(p.rounded_hours)}</strong>`:''}</td>
        <td>${p.notes||''}</td>
        <td><div class="entry-actions"><button class="btn-small btn-secondary" type="button" onclick="openEditEntry(${p.id})">Edit</button><button class="btn-small btn-danger" type="button" onclick="deleteEntry(${p.id})">Delete</button><button class="btn-small" type="button" onclick="showEntryHistory(${p.id})">${historyButtonLabel(p)}</button></div></td>
      </tr>`).join('');
    return `
      <tr>
        <td><div class="employee-summary-name"><button id="payroll-toggle-${x.id}" class="payroll-employee-toggle" type="button" aria-expanded="false" onclick="togglePayrollEmployee(${x.id})"><span class="toggle-arrow">▶</span> ${empName(x.id)}</button>${statusBadge}</div></td>
        <td>${x.shifts.length}</td>
        <td>${hrs(x.raw)}</td>
        <td><strong>${hrs(x.rounded)}</strong></td>
      </tr>
      <tr id="payroll-detail-${x.id}" class="hidden payroll-detail-row">
        <td colspan="4">
          <div class="payroll-detail-box">
            <table class="table payroll-shift-table">
              <thead><tr><th>Day / Date</th><th>Clock In</th><th>Clock Out</th><th>Raw</th><th>Rounded</th><th>Notes</th><th>Actions</th></tr></thead>
              <tbody>${detail}</tbody>
            </table>
          </div>
        </td>
      </tr>`;
  }).join('');
  const totalRow=!timesheetEmployeeId()?`
    <tr class="timesheet-total-row">
      <td><strong>TOTAL</strong></td>
      <td><strong>${shifts}</strong></td>
      <td><strong>${hrs(raw)}</strong></td>
      <td><strong>${hrs(rounded)}</strong></td>
    </tr>`:'';
  payrollBody.innerHTML=employeeRows+totalRow;
  for(const x of data){
    if(x.shifts.some(isStaleOpenShift)){
      const row=document.getElementById('payroll-detail-'+x.id);
      const btn=document.getElementById('payroll-toggle-'+x.id);
      if(row&&btn){row.classList.remove('hidden');btn.querySelector('.toggle-arrow').textContent='▼';btn.setAttribute('aria-expanded','true')}
    }
  }
}

function csvCellTimesheet(v){return '"'+String(v??'').replace(/"/g,'""')+'"'}
function exportTimesheetCsv(){
  const range=getTimesheetRange();
  if(!range||range.end<range.start){alert('Choose a valid date range first.');return}
  const rows=timesheetRows().slice().sort((a,b)=>new Date(a.clock_in)-new Date(b.clock_in));
  const out=[['Employee','Day / Date','Clock In','Clock Out','Raw Hours','Rounded Hours','Notes','Source']];
  for(const p of rows)out.push([empName(p.employee_id),payrollDay(p.clock_in),fmtDate(p.clock_in),p.clock_out?fmtDate(p.clock_out):'OPEN',p.clock_out?Number(p.raw_hours||0).toFixed(2):'',p.clock_out?Number(p.rounded_hours||0).toFixed(2):'',p.notes||'',p.source||'']);
  const blob=new Blob([out.map(r=>r.map(csvCellTimesheet).join(',')).join('\n')],{type:'text/csv'});
  const a=document.createElement('a');a.href=URL.createObjectURL(blob);
  const who=timesheetEmployeeId()?'-'+empName(timesheetEmployeeId()).replace(/[^a-z0-9]+/gi,'-').replace(/^-|-$/g,'').toLowerCase():'';
  a.download=`timesheets${who}-${timesheetDateValue(range.start)}-to-${timesheetDateValue(range.end)}.csv`;
  a.click();URL.revokeObjectURL(a.href);
}

function replaceTimesheetButton(id,handler){
  const old=document.getElementById(id);if(!old)return null;
  const fresh=old.cloneNode(true);old.replaceWith(fresh);fresh.addEventListener('click',handler);return fresh;
}

(function wireTimesheets(){
  replaceTimesheetButton('loadPayroll',renderPayroll);
  replaceTimesheetButton('exportCsvBtn',exportTimesheetCsv);
  document.getElementById('timesheetRangeMode')?.addEventListener('change',()=>{updateTimesheetRangeControls();renderPayroll()});
  document.getElementById('timesheetEmployee')?.addEventListener('change',renderPayroll);
  document.getElementById('payrollPeriod')?.addEventListener('change',()=>{if(document.getElementById('timesheetRangeMode')?.value==='period')renderPayroll()});
  document.querySelectorAll('nav button[data-page]').forEach(btn=>btn.addEventListener('click',()=>setTimeout(syncTimesheetVisibility,0)));
  updateTimesheetRangeControls();
  syncTimesheetVisibility();
})();