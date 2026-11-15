import java.util.*;
import java.util.prefs.*;
/** Isolate CubeMX test preferences in memory instead of the Windows registry. */
public final class WorkspacePreferencesFactory implements PreferencesFactory {
    private final Preferences root=new MemoryPreferences(null, "");
    public Preferences userRoot(){return root;}
    public Preferences systemRoot(){return root;}
    private static final class MemoryPreferences extends AbstractPreferences {
        private final Map<String,String> data=new HashMap<>();
        private final Map<String,MemoryPreferences> children=new HashMap<>();
        MemoryPreferences(AbstractPreferences parent,String name){super(parent,name);}
        protected void putSpi(String k,String v){data.put(k,v);}
        protected String getSpi(String k){return data.get(k);}
        protected void removeSpi(String k){data.remove(k);}
        protected void removeNodeSpi(){data.clear();children.clear();}
        protected String[] keysSpi(){return data.keySet().toArray(new String[0]);}
        protected String[] childrenNamesSpi(){return children.keySet().toArray(new String[0]);}
        protected AbstractPreferences childSpi(String n){return children.computeIfAbsent(n,k->new MemoryPreferences(this,k));}
        protected void syncSpi(){}
        protected void flushSpi(){}
    }
}
