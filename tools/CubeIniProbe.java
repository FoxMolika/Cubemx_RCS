import com.st.components.util.ParameterSetting;
/** Test only: parse tool output with the actual CubeMX INI implementation. */
public final class CubeIniProbe {
 public static void main(String[] args)throws Exception {
  ParameterSetting p=new ParameterSetting();p.load(args[0]);
  if(!p.getProperty("Others","HALModule","").contains("CAN"))throw new Exception("CAN missing");
  if(p.getProperty("ProjectFiles","HeaderPath","")==null)throw new Exception("ProjectFiles read failed");
  if(p.getProperty("Groups","",true)==null)throw new Exception("Groups missing");
  System.out.println("PASS CubeMX native INI parse");
 }
}
