import com.st.ipmodeconfigmanager.xml.MxPackages;
/** Development check using the validator shipped with this installed CubeMX. */
public class ValidateCubeXml {
    public static void main(String[] args) {
        MxPackages p=new MxPackages();
        System.out.println("MODES valid="+p.checkModeSchema(args[0]));
        System.out.println(p.getModeSchemaErrorList());
        System.out.println("CONFIGS valid="+p.checkConfigSchema(args[1]));
        System.out.println(p.getConfigSchemaErrorList());
    }
}
