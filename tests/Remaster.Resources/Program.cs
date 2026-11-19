using Ryujinx.Graphics.Gpu;
const ulong GiB = 1024UL*1024*1024, MiB = 1024UL*1024;
int passed=0;
void Check(bool ok,string name) { if(!ok) throw new Exception("FAIL: "+name); Console.WriteLine("PASS: "+name);passed++; }
Check(ResourceBudget.ConserveMemory(6*GiB), "6 GiB host selects reduced retention");
Check(ResourceBudget.ConserveMemory(8*GiB), "8 GiB host selects reduced retention");
Check(!ResourceBudget.ConserveMemory(16*GiB), "larger host keeps normal retention policy");
Check(!ResourceBudget.ConserveMemory(0), "unknown memory does not misidentify host");
Check(ResourceBudget.TextureCacheBytes(6*GiB,3*GiB,true,true)==256*MiB,"6 GiB shared GPU: 256 MiB texture retention");
Check(ResourceBudget.TextureCacheBytes(6*GiB,2*GiB,false,true)==256*MiB,"6 GiB dedicated GPU: retain RAM for game/OS");
Check(ResourceBudget.TextureCacheBytes(16*GiB,8*GiB,true,false)==512*MiB,"shared GPU heap is not treated as dedicated VRAM");
Check(ResourceBudget.TextureCacheBytes(32*GiB,8*GiB,false,false)==4*GiB,"ordinary dedicated GPU budget unchanged");
Check(ResourceBudget.TextureCacheBytes(GiB,8*GiB,false,false)==GiB,"tiny host budget cannot underflow");
Check(ResourceBudget.TextureCacheBytes(0,0,false,false)==512*MiB,"unknown capacities have bounded fallback");
Check(ResourceBudget.HomebrewMemoryBytes(6*GiB)==2*GiB,"constrained homebrew process gets a consistent 2 GiB budget");
Check(ResourceBudget.HomebrewMemoryBytes(16*GiB)==0,"unconstrained host keeps original process memory limit");
Check(ResourceBudget.HomebrewMemoryBytes(8*GiB)==0,"8 GiB host keeps the original guest memory pool");
Check(ResourceBudget.HomebrewMemoryBytes(0)==0,"unknown capacity cannot impose a guest memory limit");
Console.WriteLine($"{passed} resource checks passed.");
