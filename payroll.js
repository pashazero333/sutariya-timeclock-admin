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
    if(!m.has(p.employee_id))m.set(p.employee_id,{id:p.employee_id,count:0,raw:0,rounded:0,shifts:[]});
    const x=m.get(p.employee_id);
    x.shifts.push(p);
    if(p.clock_out){
      x.count++;
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
  return v?new Date(v).toLocaleTimeString('en-US',{hour:'numeric',minute:'2-digit'}):'<span class="pill green">ACTIVE</span>';
}

function togglePayrollEmployee(id){
  const row=document.getElementById('payroll-detail-'+id);
  const btn=document.getElementById('payroll-toggle-'+id);
  if(!row||!btn)return;
  const opening=row.classList.contains('hidden');
  row.classList.toggle('hidden',!opening);
  btn.textContent=(opening?'▼ ':'▶ ')+empName(id);
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
  if(btn)btn.textContent=custom?'Generate Report':'Apply';
  if(custom){
    const per=selectedPeriod('payrollPeriod');
    const start=document.getElementById('timesheetCustomStart');
    const end=document.getElementById('timesheetCustomEnd');
    if(start&&!start.value)start.value=timesheetDateValue(per.start);
    if(end&&!end.value)end.value=timesheetDateValue(per.end);
  }
}

function timesheetRangeText(){
  const range=getTimesheetRange();
  if(!range)return 'Choose a valid start and end date.';
  const who=timesheetEmployeeId()?empName(timesheetEmployeeId()):'All employees';
  return `${range.custom?'Custom report':'Payroll period'} · ${fmtShort(range.start)} – ${fmtShort(range.end)} · ${who}`;
}

function renderPayroll(){
  syncTimesheetEmployeeOptions();
  updateTimesheetRangeControls();
  const range=getTimesheetRange();
  const label=document.getElementById('timesheetRangeLabel');
  if(label)label.textContent=timesheetRangeText();
  if(range&&range.end<range.start){
    payrollRaw.textContent='Raw 0.00 hrs';
    payrollRounded.textContent='Rounded 0.00 hrs';
    payrollBody.innerHTML='<tr><td colspan="4" class="empty">End date must be on or after the start date.</td></tr>';
    return;
  }
  const data=payrollData();
  const raw=data.reduce((a,x)=>a+x.raw,0);
  const rounded=data.reduce((a,x)=>a+x.rounded,0);
  payrollRaw.textContent=`Raw ${raw.toFixed(2)} hrs`;
  payrollRounded.textContent=`Rounded ${rounded.toFixed(2)} hrs`;
  if(!data.length){
    payrollBody.innerHTML='<tr><td colspan="4" class="empty">No time entries in this date range.</td></tr>';
    return;
  }
  payrollBody.innerHTML=data.map(x=>{
    const detail=x.shifts.slice().sort((a,b)=>new Date(a.clock_in)-new Date(b.clock_in)).map(p=>`
      <tr>
        <td>${payrollDay(p.clock_in)}</td>
        <td>${payrollTime(p.clock_in)}</td>
        <td>${payrollTime(p.clock_out)}</td>
        <td>${p.clock_out?hrs(p.raw_hours):''}</td>
        <td>${p.clock_out?`<strong>${hrs(p.rounded_hours)}</strong>`:''}</td>
        <td>${p.notes||''}</td>
        <td><div class="entry-actions"><button class="btn-small btn-secondary" type="button" onclick="openEditEntry(${p.id})">Edit</button><button class="btn-small btn-danger" type="button" onclick="deleteEntry(${p.id})">Delete</button><button class="btn-small" type="button" onclick="showEntryHistory(${p.id})">History</button></div></td>
      </tr>`).join('');
    return `
      <tr>
        <td><button id="payroll-toggle-${x.id}" class="payroll-employee-toggle" type="button" aria-expanded="false" onclick="togglePayrollEmployee(${x.id})">▶ ${empName(x.id)}</button></td>
        <td>${x.count}</td>
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
}

function csvCellTimesheet(v){return '"'+String(v??'').replace(/"/g,'""')+'"'}
function exportTimesheetCsv(){
  const range=getTimesheetRange();
  if(!range||range.end<range.start){alert('Choose a valid date range first.');return}
  const rows=timesheetRows().slice().sort((a,b)=>new Date(a.clock_in)-new Date(b.clock_in));
  const out=[['Employee','Day / Date','Clock In','Clock Out','Raw Hours','Rounded Hours','Notes','Source']];
  for(const p of rows)out.push([empName(p.employee_id),payrollDay(p.clock_in),fmtDate(p.clock_in),p.clock_out?fmtDate(p.clock_out):'ACTIVE',p.clock_out?Number(p.raw_hours||0).toFixed(2):'',p.clock_out?Number(p.rounded_hours||0).toFixed(2):'',p.notes||'',p.source||'']);
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
  updateTimesheetRangeControls();
})();
